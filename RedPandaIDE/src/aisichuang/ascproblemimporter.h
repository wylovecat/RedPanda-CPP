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
#ifndef ASCPROBLEMIMPORTER_H
#define ASCPROBLEMIMPORTER_H

#include "../problems/ojproblemset.h"
#include "asctypes.h"

#include <QObject>
#include <QString>

class AscClient;

/** What a problem was reached through, so files land in the right folder and
 *  a later submission can find its way back to the site. */
struct AscImportContext
{
    AscProblemSource source;
    QString taskId;      // testId or workId; empty for the question bank
    QString taskName;
    QString classId;
    QString className;

    AscImportContext(): source(AscProblemSource::Bank) {}

    bool isTask() const { return source != AscProblemSource::Bank; }
};

/** Everything the caller needs after a successful import. */
struct AscImportedProblem
{
    POJProblem problem;
    AscStatement statement;
    AscImportContext context;
    QString sourcePath;
    QString folder;
};

/**
 * Turns an online problem into a local one: fetches the statement and its
 * sample cases, writes "<displayId>.cpp" plus "<displayId>_<n>.in/.out" next
 * to it, and builds an OJProblem so the IDE's existing problem-set runner can
 * judge the code against those samples.
 */
class AscProblemImporter: public QObject
{
    Q_OBJECT
public:
    explicit AscProblemImporter(AscClient* client, QObject* parent = nullptr);

    /**
     * Fetch and write one problem.
     *
     * The destination folder, the code template and whether to keep a markdown
     * copy of the statement are passed in rather than read from the settings:
     * the caller (the panel) owns that policy, and this class stays testable
     * without an application settings file.
     */
    void importProblem(const QString& displayId, const QString& title,
                       const AscImportContext& context, const QString& folder,
                       const QString& templateText, bool saveMarkdown);

    /** Task names come from the site and end up in paths; strip what a file system dislikes. */
    static QString sanitizeName(const QString& name);

    /**
     * Write the files of one problem and build the IDE's own problem object.
     *
     * Public and free of any network or settings state so it can be tested with
     * a recorded statement:  templateText seeds the answer source when the file
     * does not exist yet, and  saveMarkdown writes the statement next to it.
     */
    static AscImportedProblem writeProblem(const AscStatement& statement,
                                           const AscImportContext& context,
                                           const QString& folder,
                                           const QString& templateText,
                                           bool saveMarkdown);

signals:
    void importStarted(const QString& displayId);
    void importFailed(const QString& displayId, const QString& message);
    void importFinished(const AscImportedProblem& imported);

private:
    AscClient* mClient;
};

#endif // ASCPROBLEMIMPORTER_H
