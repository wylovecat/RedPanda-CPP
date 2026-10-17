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
#include "ascstatement.h"

#include "ascenvelope.h"

namespace Asc {

AscStatement statementFromOjDto(const QJsonObject& dto, const QString& displayId, const QString& title)
{
    AscStatement statement;
    statement.displayId = strOf(dto, {QStringLiteral("_id")});
    if (statement.displayId.isEmpty())
        statement.displayId = displayId;
    statement.title = strOf(dto, {QStringLiteral("title")});
    if (statement.title.isEmpty())
        statement.title = title;
    statement.description = strOf(dto, {QStringLiteral("description")});
    statement.inputDescription = strOf(dto, {QStringLiteral("input_description"),
                                             QStringLiteral("inputFormat"), QStringLiteral("input")});
    statement.outputDescription = strOf(dto, {QStringLiteral("output_description"),
                                              QStringLiteral("outputFormat"), QStringLiteral("output")});
    statement.samples = pickSamples(dto.value(QStringLiteral("samples")).toArray());
    statement.hint = strOf(dto, {QStringLiteral("hint")});
    statement.prompt = strOf(dto, {QStringLiteral("titlePrompt"), QStringLiteral("prompt")});
    statement.timeLimitMs = numOf(dto, {QStringLiteral("time_limit"), QStringLiteral("timeLimit")}, 0);
    statement.memoryLimitMb = numOf(dto, {QStringLiteral("memory_limit"), QStringLiteral("memoryLimit")}, 0);
    return statement;
}

AscStatement statementFromDetail(const QJsonObject& dto, const QString& displayId, const QString& title)
{
    AscStatement statement;
    statement.samples = pickSamples(dto.value(QStringLiteral("samples")).toArray());
    if (statement.samples.isEmpty()) {
        // Some problems carry one single sample as two top level fields.
        QJsonObject single;
        single.insert(QStringLiteral("input"), dto.value(QStringLiteral("input")));
        single.insert(QStringLiteral("output"), dto.value(QStringLiteral("output")));
        QJsonArray array;
        array.append(single);
        statement.samples = pickSamples(array);
    }
    statement.displayId = strOf(dto, {QStringLiteral("displayId"), QStringLiteral("_id")});
    if (statement.displayId.isEmpty())
        statement.displayId = displayId;
    statement.title = strOf(dto, {QStringLiteral("title"), QStringLiteral("titleName")});
    if (statement.title.isEmpty())
        statement.title = title;
    statement.description = strOf(dto, {QStringLiteral("titleDescribe"), QStringLiteral("description")});
    statement.inputDescription = strOf(dto, {QStringLiteral("inputForm"), QStringLiteral("input_description")});
    statement.outputDescription = strOf(dto, {QStringLiteral("outputForm"), QStringLiteral("output_description")});
    statement.hint = strOf(dto, {QStringLiteral("titleBackground"), QStringLiteral("hint")});
    statement.prompt = strOf(dto, {QStringLiteral("titlePrompt"), QStringLiteral("prompt")});
    statement.timeLimitMs = numOf(dto, {QStringLiteral("timeLimit"), QStringLiteral("time_limit")}, 0);
    statement.memoryLimitMb = numOf(dto, {QStringLiteral("memoryLimit"), QStringLiteral("memory_limit")}, 0);
    // The site's own markdown statement; preferred for display when present.
    statement.rawMarkdown = strOf(dto, {QStringLiteral("all")});
    return statement;
}

QString statementToMarkdown(const AscStatement& statement)
{
    QStringList lines;
    lines << (QStringLiteral("# %1 %2").arg(statement.displayId, statement.title)).trimmed();

    QStringList limits;
    if (statement.timeLimitMs > 0)
        limits << QStringLiteral("时间限制 %1 ms").arg(statement.timeLimitMs);
    if (statement.memoryLimitMb > 0)
        limits << QStringLiteral("内存限制 %1 MB").arg(statement.memoryLimitMb);
    if (!limits.isEmpty()) {
        lines << QString() << (QStringLiteral("> %1").arg(limits.join(QStringLiteral(" · "))));
    }

    struct Section { QString title; QString body; };
    const Section sections[] = {
        {QStringLiteral("题目描述"), statement.description},
        {QStringLiteral("输入格式"), statement.inputDescription},
        {QStringLiteral("输出格式"), statement.outputDescription},
    };
    for (const Section& section : sections) {
        if (section.body.trimmed().isEmpty())
            continue;
        lines << QString() << QStringLiteral("## %1").arg(section.title)
              << QString() << section.body.trimmed();
    }

    if (!statement.samples.isEmpty()) {
        lines << QString() << QStringLiteral("## 样例");
        for (int i = 0; i < statement.samples.count(); ++i) {
            const AscSample& sample = statement.samples[i];
            lines << QString() << QStringLiteral("### 样例 %1").arg(i + 1)
                  << QString() << QStringLiteral("**输入**") << QString()
                  << QStringLiteral("```") << sample.input.trimmed() << QStringLiteral("```")
                  << QString() << QStringLiteral("**输出**") << QString()
                  << QStringLiteral("```") << sample.output.trimmed() << QStringLiteral("```");
        }
    }

    if (!statement.prompt.trimmed().isEmpty()) {
        lines << QString() << QStringLiteral("## 提示") << QString() << statement.prompt.trimmed();
    }
    if (!statement.hint.trimmed().isEmpty()) {
        lines << QString() << QStringLiteral("## 说明") << QString() << statement.hint.trimmed();
    }
    return lines.join(QLatin1Char('\n')) + QLatin1Char('\n');
}

}
