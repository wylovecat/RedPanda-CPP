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
#include "ascenvelope.h"

#include <QFileInfo>
#include <QRegularExpression>

namespace Asc {

static QString valueText(const QJsonValue& value, bool* ok)
{
    *ok = false;
    if (value.isString()) {
        *ok = true;
        return value.toString();
    }
    if (value.isDouble()) {
        double d = value.toDouble();
        if (d == double(qint64(d))) {
            *ok = true;
            return QString::number(qint64(d));
        }
        *ok = true;
        return QString::number(d);
    }
    if (value.isBool()) {
        *ok = true;
        return value.toBool() ? "1" : "0";
    }
    return QString();
}

QString strOf(const QJsonObject& obj, const QStringList& keys)
{
    for (const QString& key : keys) {
        if (!obj.contains(key))
            continue;
        bool ok = false;
        QString text = valueText(obj.value(key), &ok);
        if (!ok)
            continue;
        if (text.trimmed().isEmpty())
            continue;
        return text;
    }
    return QString();
}

qlonglong numOf(const QJsonObject& obj, const QStringList& keys, qlonglong defaultValue)
{
    for (const QString& key : keys) {
        if (!obj.contains(key))
            continue;
        const QJsonValue& value = obj.value(key);
        if (value.isDouble())
            return qlonglong(value.toDouble());
        if (value.isString()) {
            QString text = value.toString().trimmed();
            bool ok = false;
            qlonglong number = text.toLongLong(&ok);
            if (ok)
                return number;
        }
    }
    return defaultValue;
}

bool boolOf(const QJsonObject& obj, const QStringList& keys, bool defaultValue)
{
    for (const QString& key : keys) {
        if (!obj.contains(key))
            continue;
        const QJsonValue& value = obj.value(key);
        if (value.isBool())
            return value.toBool();
        if (value.isDouble())
            return value.toDouble() != 0;
        if (value.isString()) {
            QString text = value.toString().trimmed().toLower();
            if (text.isEmpty())
                continue;
            if (text == "false" || text == "0" || text == "no")
                return false;
            return true;
        }
    }
    return defaultValue;
}

QJsonArray arrayOf(const QJsonValue& data)
{
    if (data.isArray())
        return data.toArray();
    if (data.isObject()) {
        QJsonObject obj = data.toObject();
        for (const QString& key : {QStringLiteral("records"), QStringLiteral("list"), QStringLiteral("rows")}) {
            if (obj.value(key).isArray())
                return obj.value(key).toArray();
        }
    }
    return QJsonArray();
}

bool envelopeSuccess(const QJsonObject& body, qint64* code, QString* message)
{
    if (!body.contains("code")) {
        if (code)
            *code = -1;
        if (message)
            *message = QStringLiteral("Unexpected response: the site did not return a status code.");
        return false;
    }
    qint64 status = numOf(body, {QStringLiteral("code")}, -1);
    if (code)
        *code = status;
    if (message)
        *message = strOf(body, {QStringLiteral("msg"), QStringLiteral("message")});
    return status == 200;
}

bool isAuthFailure(qint64 code, const QString& message)
{
    if (code == 401)
        return true;
    // The site's own wordings for a dead session.
    static const QStringList needles = {
        QStringLiteral("请登录"), QStringLiteral("请先登录"), QStringLiteral("需要登录"),
        QStringLiteral("未登录"), QStringLiteral("登录已过期"), QStringLiteral("会话已过期"),
        QStringLiteral("登录失效"), QStringLiteral("重新登录")
    };
    for (const QString& needle : needles) {
        if (message.contains(needle))
            return true;
    }
    return false;
}

QString normalizeOutput(const QString& text)
{
    QString unified = text;
    unified.replace(QStringLiteral("\r\n"), QStringLiteral("\n"));
    unified.replace(QLatin1Char('\r'), QLatin1Char('\n'));
    const QStringList rawLines = unified.split(QLatin1Char('\n'));
    QStringList lines;
    lines.reserve(rawLines.count());
    for (const QString& line : rawLines) {
        QString trimmed = line;
        while (!trimmed.isEmpty()
                && (trimmed.endsWith(QLatin1Char(' ')) || trimmed.endsWith(QLatin1Char('\t')))) {
            trimmed.chop(1);
        }
        lines.append(trimmed);
    }
    while (!lines.isEmpty() && lines.last().isEmpty())
        lines.removeLast();
    return lines.join(QLatin1Char('\n'));
}

QString normalizeVerdict(const QString& raw)
{
    QString text = raw.trimmed();
    if (text.isEmpty())
        return QStringLiteral("?");

    QString compact = text;
    compact.remove(QRegularExpression(QStringLiteral("[^0-9A-Za-z]")));
    compact = compact.toUpper();

    static const QStringList exact = {"AC", "WA", "TLE", "MLE", "RE", "CE", "PE"};
    if (exact.contains(compact))
        return compact;
    if (compact == QStringLiteral("OK"))
        return QStringLiteral("AC");
    if (compact.startsWith(QStringLiteral("ACCEPT")))
        return QStringLiteral("AC");
    if (compact.startsWith(QStringLiteral("WRONG")))
        return QStringLiteral("WA");
    if (compact.startsWith(QStringLiteral("TIME")))
        return QStringLiteral("TLE");
    if (compact.startsWith(QStringLiteral("MEMORY")))
        return QStringLiteral("MLE");
    if (compact.startsWith(QStringLiteral("RUNTIME")))
        return QStringLiteral("RE");
    if (compact.startsWith(QStringLiteral("COMPILE")))
        return QStringLiteral("CE");
    if (compact.startsWith(QStringLiteral("PRESENTATION")))
        return QStringLiteral("PE");
    // Verdicts often arrive with a measurement glued on, such as
    // "TLE (2001ms)" -> "TLE2001MS".
    for (const QString& code : exact) {
        if (compact.startsWith(code))
            return code;
    }

    // The site writes its verdicts in Chinese.
    struct Token { const char* needle; const char* verdict; };
    static const Token tokens[] = {
        {"编译", "CE"},
        {"超时", "TLE"},
        {"时间超限", "TLE"},
        {"超出时间", "TLE"},
        {"内存", "MLE"},
        {"空间超限", "MLE"},
        {"运行错误", "RE"},
        {"答案错误", "WA"},
        {"格式", "PE"},
        {"答案正确", "AC"},
        {"通过", "AC"},
    };
    for (const Token& token : tokens) {
        if (text.contains(QString::fromUtf8(token.needle)))
            return QString::fromUtf8(token.verdict);
    }
    return QStringLiteral("?");
}

QString verdictOfRecord(const QJsonObject& record)
{
    static const QStringList keys = {
        QStringLiteral("subStatic"), QStringLiteral("substatic"),
        QStringLiteral("status"), QStringLiteral("feedback"),
        QStringLiteral("result"), QStringLiteral("verdict")
    };
    for (const QString& key : keys) {
        if (!record.contains(key))
            continue;
        if (!record.value(key).isString())
            continue;
        QString text = record.value(key).toString().trimmed();
        if (text.isEmpty())
            continue;
        return normalizeVerdict(text);
    }
    return QStringLiteral("?");
}

bool isFinalResult(const AscSubmissionResult& result)
{
    if (result.judging)
        return false;
    bool ok = false;
    qlonglong id = result.id.toLongLong(&ok);
    return (ok && id > 0) || !result.rawStatus.trimmed().isEmpty();
}

QString maskAccount(const QString& phone)
{
    if (phone.length() < 5)
        return phone;
    return phone.left(3) + QStringLiteral("****") + phone.right(2);
}

bool looksLikeProblemId(const QString& stem)
{
    static const QRegularExpression pattern(QStringLiteral("^[A-Za-z]\\w*\\d+$"));
    return pattern.match(stem).hasMatch();
}

QString stemOf(const QString& fileName)
{
    return QFileInfo(fileName).baseName();
}

QString formatBytes(qlonglong bytes)
{
    if (bytes < 0)
        return QStringLiteral("?");
    if (bytes < 1024)
        return QStringLiteral("%1 B").arg(bytes);
    if (bytes < 1024 * 1024)
        return QStringLiteral("%1 KB").arg(double(bytes) / 1024, 0, 'f', 1);
    return QStringLiteral("%1 MB").arg(double(bytes) / 1024 / 1024, 0, 'f', 1);
}

QVector<AscProblemRef> problemRefsFrom(const QJsonValue& data)
{
    QVector<AscProblemRef> result;
    const QJsonArray rows = arrayOf(data);
    for (const QJsonValue& value : rows) {
        QJsonObject row = value.toObject();
        AscProblemRef ref;
        ref.problemId = strOf(row, {QStringLiteral("id"), QStringLiteral("questionId"), QStringLiteral("problemId")});
        ref.displayId = strOf(row, {QStringLiteral("displayId"), QStringLiteral("questionCode")});
        ref.title = strOf(row, {QStringLiteral("title"), QStringLiteral("titleName"), QStringLiteral("name")});
        if (ref.displayId.isEmpty() && ref.problemId.isEmpty())
            continue;
        result.append(ref);
    }
    return result;
}

QVector<AscClassInfo> classesFrom(const QJsonValue& data)
{
    QVector<AscClassInfo> result;
    const QJsonArray rows = arrayOf(data);
    for (const QJsonValue& value : rows) {
        QJsonObject row = value.toObject();
        AscClassInfo info;
        info.classId = strOf(row, {QStringLiteral("id"), QStringLiteral("classId")});
        info.className = strOf(row, {QStringLiteral("className"), QStringLiteral("name")});
        info.classroomName = strOf(row, {QStringLiteral("classroomName")});
        if (info.classId.isEmpty())
            continue;
        result.append(info);
    }
    return result;
}

QVector<AscTaskInfo> tasksFrom(const QJsonValue& data, AscProblemSource source)
{
    const bool isWork = source == AscProblemSource::Work;
    // Note the all-lowercase ids: the site really does return "workid"/"testid".
    const QStringList idKeys = isWork
            ? QStringList{QStringLiteral("workid"), QStringLiteral("workId")}
            : QStringList{QStringLiteral("testid"), QStringLiteral("testId")};
    const QStringList nameKeys = isWork
            ? QStringList{QStringLiteral("workName"), QStringLiteral("name")}
            : QStringList{QStringLiteral("testName"), QStringLiteral("name")};

    QVector<AscTaskInfo> result;
    const QJsonArray rows = arrayOf(data);
    for (const QJsonValue& value : rows) {
        QJsonObject row = value.toObject();
        AscTaskInfo info;
        info.source = source;
        info.taskId = strOf(row, idKeys);
        info.taskName = strOf(row, nameKeys);
        info.createTime = strOf(row, {QStringLiteral("createTime")});
        info.endTime = strOf(row, {QStringLiteral("endTime")});
        info.problemCount = int(numOf(row, {QStringLiteral("sum"), QStringLiteral("questionCount")}, 0));
        info.submitted = boolOf(row, {QStringLiteral("isSubmit")}, false);
        if (info.taskId.isEmpty())
            continue;
        result.append(info);
    }
    return result;
}

QVector<AscTaskProblem> taskProblemsFrom(const QJsonValue& data, AscProblemSource source)
{
    QVector<AscTaskProblem> result;
    const QJsonArray rows = arrayOf(data);
    for (const QJsonValue& value : rows) {
        QJsonObject row = value.toObject();
        // quesTypeId 5 is a programming problem; anything else cannot be judged from code.
        if (numOf(row, {QStringLiteral("quesTypeId")}, 0) != 5)
            continue;
        AscTaskProblem problem;
        problem.tkId = strOf(row, {QStringLiteral("tkId"), QStringLiteral("id")});
        problem.testSubId = strOf(row, {QStringLiteral("testSubId"), QStringLiteral("workSubId")});
        problem.displayId = strOf(row, {QStringLiteral("displayId"), QStringLiteral("questionCode")});
        problem.title = strOf(row, {QStringLiteral("title"), QStringLiteral("titleName")});
        // Tests spell the done flag "isDo", homework spells it "is_do".
        const QStringList doneKeys = source == AscProblemSource::Work
                ? QStringList{QStringLiteral("is_do"), QStringLiteral("isDo")}
                : QStringList{QStringLiteral("isDo"), QStringLiteral("is_do")};
        problem.done = numOf(row, doneKeys, 0) != 0;
        problem.records = int(numOf(row, {QStringLiteral("records")}, 0));
        if (problem.displayId.isEmpty())
            continue;
        result.append(problem);
    }
    return result;
}

AscSubmissionResult submissionFrom(const QJsonValue& data)
{
    AscSubmissionResult result;
    if (!data.isObject())
        return result;
    QJsonObject record = data.toObject();
    result.id = strOf(record, {QStringLiteral("id"), QStringLiteral("tkSubId")});
    result.time = strOf(record, {QStringLiteral("time"), QStringLiteral("subTime")});
    result.rawStatus = strOf(record, {QStringLiteral("subStatic"), QStringLiteral("substatic"),
                                     QStringLiteral("status")});
    result.verdict = verdictOfRecord(record);
    result.feedback = strOf(record, {QStringLiteral("compileFeedback"), QStringLiteral("errInfo")});
    // subIsPass == 7 means the judge has not finished yet.
    result.isPass = numOf(record, {QStringLiteral("subIsPass"), QStringLiteral("isPass")}, 0);
    result.judging = result.isPass == 7;
    result.score = double(numOf(record, {QStringLiteral("subScore"), QStringLiteral("score")}, 0));
    result.timeMs = numOf(record, {QStringLiteral("timeCost"), QStringLiteral("time_cost")}, 0);
    result.memoryBytes = numOf(record, {QStringLiteral("memoryCost"), QStringLiteral("memory_cost")}, 0);
    return result;
}

QString submissionIdFrom(const QJsonValue& data, const QString& message)
{
    if (data.isDouble()) {
        double value = data.toDouble();
        return QString::number(qlonglong(value));
    }
    if (data.isString() && !data.toString().trimmed().isEmpty())
        return data.toString().trimmed();
    return message.trimmed();
}

QVector<AscSample> pickSamples(const QJsonArray& samples)
{
    static const QStringList inputKeys = {
        QStringLiteral("input"), QStringLiteral("sampleInput"),
        QStringLiteral("in"), QStringLiteral("inputSample")
    };
    static const QStringList outputKeys = {
        QStringLiteral("output"), QStringLiteral("sampleOutput"),
        QStringLiteral("out"), QStringLiteral("outputSample")
    };

    QVector<AscSample> result;
    for (const QJsonValue& value : samples) {
        QJsonObject item = value.toObject();
        AscSample sample;
        sample.input = strOf(item, inputKeys);
        sample.output = strOf(item, outputKeys);
        if (sample.input.isEmpty() && sample.output.isEmpty())
            continue;
        result.append(sample);
    }
    return result;
}

}
