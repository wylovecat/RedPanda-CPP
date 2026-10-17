/*
 * Copyright (C) 2020-2026 Roy Qu (royqh1979@gmail.com)
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */
#include "ascproblemimporter.h"

#include "ascclient.h"
#include "ascenvelope.h"
#include "ascstatement.h"

#include "../utils.h"

#include <QDir>
#include <QRegularExpression>

/** Used when the site does not state a limit, so a hung program still ends. */
static const qlonglong ASC_FALLBACK_TIME_LIMIT_MS = 2000;

AscProblemImporter::AscProblemImporter(AscClient* client, QObject* parent):
    QObject(parent),
    mClient(client)
{

}

QString AscProblemImporter::sanitizeName(const QString& name)
{
    static const QRegularExpression forbidden(QStringLiteral("[\\\\/:*?\"<>|\\r\\n\\t]"));
    QString result = name;
    result.replace(forbidden, QStringLiteral("_"));
    result = result.trimmed();
    while (result.startsWith(QLatin1Char('.')))
        result.remove(0, 1);
    if (result.isEmpty())
        result = QStringLiteral("untitled");
    if (result.length() > 60)
        result = result.left(60);
    return result;
}

void AscProblemImporter::importProblem(const QString& displayId, const QString& title,
                                       const AscImportContext& context, const QString& folder,
                                       const QString& templateText, bool saveMarkdown)
{
    emit importStarted(displayId);

    mClient->fetchStatement(displayId, title,
            [this, displayId, context, folder, templateText, saveMarkdown](const AscStatement& statement) {
        QDir dir;
        if (!dir.mkpath(folder)) {
            emit importFailed(displayId, tr("Could not create the folder %1.").arg(folder));
            return;
        }
        AscImportedProblem imported = writeProblem(statement, context, folder,
                                                  templateText, saveMarkdown);
        emit importFinished(imported);
    }, [displayId, this](const QString& message) {
        emit importFailed(displayId, message);
    });
}

AscImportedProblem AscProblemImporter::writeProblem(const AscStatement& statement,
                                                    const AscImportContext& context,
                                                    const QString& folder,
                                                    const QString& templateText,
                                                    bool saveMarkdown)
{
    const QString displayId = statement.displayId;

    AscImportedProblem imported;
    imported.statement = statement;
    imported.context = context;
    imported.folder = folder;
    imported.sourcePath = getFilePath(folder, displayId + QStringLiteral(".cpp"));

    // Never clobber work the user already has in place.
    if (!fileExists(imported.sourcePath))
        stringToFile(templateText, imported.sourcePath);

    POJProblem problem = std::make_shared<OJProblem>();
    problem->setName(QStringLiteral("%1 %2").arg(displayId, statement.title).trimmed());
    problem->setDescription(statement.description);
    problem->setHint(statement.prompt.isEmpty() ? statement.hint : statement.prompt);
    problem->setAnswerProgram(imported.sourcePath);

    qlonglong timeLimitMs = statement.timeLimitMs > 0
            ? statement.timeLimitMs : ASC_FALLBACK_TIME_LIMIT_MS;
    problem->setTimeLimit(size_t(timeLimitMs));
    problem->setTimeLimitUnit(ProblemTimeLimitUnit::Milliseconds);
    if (statement.memoryLimitMb > 0) {
        problem->setMemoryLimit(size_t(statement.memoryLimitMb));
        problem->setMemoryLimitUnit(ProblemMemoryLimitUnit::MB);
    }

    for (int i = 0; i < statement.samples.count(); ++i) {
        const AscSample& sample = statement.samples[i];
        QString baseName = QStringLiteral("%1_%2").arg(displayId).arg(i + 1);
        QString inputPath = getFilePath(folder, baseName + QStringLiteral(".in"));
        QString expectedPath = getFilePath(folder, baseName + QStringLiteral(".out"));

        // Inputs are written byte for byte apart from line endings; the
        // expected output gets the OJ treatment so a plain "cout << ans <<
        // endl" matches.
        QString input = sample.input;
        input.replace(QStringLiteral("\r\n"), QStringLiteral("\n"));
        input.replace(QLatin1Char('\r'), QLatin1Char('\n'));
        if (!input.endsWith(QLatin1Char('\n')))
            input += QLatin1Char('\n');
        QString expected = Asc::normalizeOutput(sample.output);
        if (!expected.endsWith(QLatin1Char('\n')))
            expected += QLatin1Char('\n');

        stringToFile(input, inputPath);
        stringToFile(expected, expectedPath);

        POJProblemCase problemCase = std::make_shared<OJProblemCase>();
        problemCase->setName(tr("Sample %1").arg(i + 1));
        problemCase->setInput(sample.input);
        problemCase->setExpected(sample.output);
        problemCase->setInputFileName(inputPath);
        problemCase->setExpectedOutputFileName(expectedPath);
        problemCase->testState = ProblemCaseTestState::NotTested;
        problemCase->setModified(false);
        problem->addCase(problemCase);
    }
    problem->setModified(false);

    if (saveMarkdown) {
        QString markdown = statement.rawMarkdown.isEmpty()
                ? Asc::statementToMarkdown(statement)
                : statement.rawMarkdown;
        stringToFile(markdown, getFilePath(folder, displayId + QStringLiteral(".md")));
    }

    imported.problem = problem;
    return imported;
}
