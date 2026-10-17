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
#ifndef ASCTYPES_H
#define ASCTYPES_H

#include <QString>
#include <QVector>

/** Where a problem came from: the question bank, a classroom test or homework. */
enum class AscProblemSource
{
    Bank,
    Test,
    Work
};

/**
 * A logged-in session.
 *
 * The site is built on Sa-Token: the credential travels back in a custom
 * header literally named "satoken" (there is no Authorization/Bearer scheme),
 * together with a "sessionid" header and, for state-changing calls, a CSRF
 * token echoed in both "X-CSRFToken" and "csrftoken".
 */
struct AscSession
{
    QString baseUrl;
    QString satoken;
    QString csrftoken;
    QString sessionId;
    QString userId;
    QString role;      // "student", "teacher" or "admin"
    QString phone;

    bool isValid() const
    {
        return !satoken.isEmpty();
    }
};

/** A question-bank search hit. */
struct AscProblemRef
{
    QString problemId;   // site internal id, used to fetch the statement and to submit
    QString displayId;   // human readable id such as A1131
    QString title;
};

/** One sample case. The site gives no index: array order is the case number. */
struct AscSample
{
    QString input;
    QString output;
};

/** A problem statement with its sample cases. */
struct AscStatement
{
    QString problemId;
    QString displayId;
    QString title;
    QString description;
    QString inputDescription;
    QString outputDescription;
    QString hint;
    QString prompt;
    QString rawMarkdown;   // the site's own markdown statement, preferred for display
    QVector<AscSample> samples;
    qlonglong timeLimitMs;
    qlonglong memoryLimitMb;

    AscStatement(): timeLimitMs(0), memoryLimitMb(0) {}
};

struct AscClassInfo
{
    QString classId;
    QString className;
    QString classroomName;
};

/** A classroom test ("班测") or a homework ("作业") listed for one class. */
struct AscTaskInfo
{
    AscProblemSource source;
    QString taskId;
    QString taskName;
    QString createTime;
    QString endTime;
    int problemCount;
    bool submitted;

    AscTaskInfo(): source(AscProblemSource::Test), problemCount(0), submitted(false) {}

    bool isWork() const { return source == AscProblemSource::Work; }
};

/** One problem inside a test or homework. */
struct AscTaskProblem
{
    QString tkId;
    QString testSubId;
    QString displayId;
    QString title;
    bool done;
    int records;

    AscTaskProblem(): done(false), records(0) {}
};

/** What the site's judge reports about a submission. */
struct AscSubmissionResult
{
    QString id;          // submission record id, used as tkSubId when registering an answer
    QString time;        // submission time, echoed back as subTime
    QString rawStatus;   // the site's own verdict text (usually Chinese)
    QString verdict;     // normalized: AC/WA/TLE/MLE/RE/CE/PE/"?"
    QString feedback;    // compile errors and the like
    bool judging;        // subIsPass == 7
    qlonglong isPass;    // the site's numeric pass flag
    double score;
    qlonglong timeMs;
    qlonglong memoryBytes;

    AscSubmissionResult(): judging(false), isPass(0), score(0), timeMs(0), memoryBytes(0) {}

    bool passed() const
    {
        return verdict == QLatin1String("AC") || isPass == 1;
    }
};

#endif // ASCTYPES_H
