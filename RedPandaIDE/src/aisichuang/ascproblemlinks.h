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
#ifndef ASCPROBLEMLINKS_H
#define ASCPROBLEMLINKS_H

#include "asctypes.h"

#include <QHash>
#include <QString>

/**
 * What a local source file corresponds to on the site.
 *
 * The IDE's own problem set does not carry the site's internal ids, nor does
 * it remember whether a problem came from the bank, a classroom test or a
 * homework. Submitting code back needs all of that, so it is kept here, keyed
 * by the absolute path of the answer source.
 */
struct AscProblemLink
{
    QString displayId;
    QString problemId;   // question-bank id used by /api/submission
    QString title;
    AscProblemSource source;
    QString taskId;
    QString taskName;
    QString classId;
    QString className;

    AscProblemLink(): source(AscProblemSource::Bank) {}

    bool isValid() const { return !displayId.isEmpty(); }
    bool isTask() const { return source != AscProblemSource::Bank; }
};

/** Persistent table of those links. */
class AscProblemLinks
{
public:
    static AscProblemLinks& instance();

    void load();
    bool save() const;

    void put(const QString& sourcePath, const AscProblemLink& link);
    void remove(const QString& sourcePath);

    /**
     * The link of a file. When nothing is stored, a file whose name looks like
     * a problem id ("A1131.cpp") is still treated as a bank problem, so
     * hand-written files can be submitted without importing them first.
     */
    AscProblemLink linkFor(const QString& sourcePath) const;

    static QString configFile();

private:
    AscProblemLinks() = default;
    AscProblemLinks(const AscProblemLinks&) = delete;
    AscProblemLinks& operator=(const AscProblemLinks&) = delete;

    QHash<QString, AscProblemLink> mLinks;
    bool mLoaded;
};

#endif // ASCPROBLEMLINKS_H
