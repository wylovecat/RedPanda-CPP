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
#include "test_aisichuang.h"

#include "../src/aisichuang/ascclient.h"
#include "../src/aisichuang/ascproblemimporter.h"
#include "../src/aisichuang/ascenvelope.h"
#include "../src/aisichuang/ascstatement.h"

#include <algorithm>

#include <qt_utils/utils.h>

#include <QEventLoop>
#include <QFile>
#include <QFileInfo>
#include <QTemporaryDir>
#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonObject>
#include <QTimer>
#include <QSslSocket>
#include <QtTest>

static QJsonObject jsonOf(const QString& text)
{
    QJsonParseError error;
    QJsonDocument document = QJsonDocument::fromJson(text.toUtf8(), &error);
    if (error.error != QJsonParseError::NoError)
        qWarning() << "bad test fixture:" << error.errorString();
    return document.object();
}

static QJsonArray arrayOf(const QString& text)
{
    return QJsonDocument::fromJson(text.toUtf8()).array();
}

/** A bare JSON array wrapped the way these endpoints return it. */
static QJsonValue bareArray(const char* text)
{
    return QJsonValue(QJsonDocument::fromJson(QByteArray(text)).array());
}

void TestAisichuang::initTestCase()
{
    mLiveEnabled = qEnvironmentVariableIsSet("ASC_LIVE");
    mPhone = qEnvironmentVariable("ASC_PHONE");
    mPassword = qEnvironmentVariable("ASC_PASSWORD");
    if (mLiveEnabled)
        qInfo() << "live aisichuang tests enabled";
}

void TestAisichuang::envelope_success_and_failure()
{
    qint64 code = 0;
    QString message;

    QVERIFY(Asc::envelopeSuccess(jsonOf(R"({"code":200,"msg":"success","data":{}})"), &code, &message));
    QCOMPARE(code, qlonglong(200));

    // A teaching site that reports "success" while handing back an error code
    // would otherwise look fine to a careless reader.
    QVERIFY(!Asc::envelopeSuccess(jsonOf(R"({"code":500,"msg":"服务器错误","data":null})"), &code, &message));
    QCOMPARE(code, qlonglong(500));
    QCOMPARE(message, QString::fromUtf8("服务器错误"));

    QVERIFY(!Asc::envelopeSuccess(jsonOf(R"({"msg":"no code"})"), &code, &message));
    QVERIFY(!Asc::envelopeSuccess(jsonOf(R"({})"), &code, &message));
}

void TestAisichuang::auth_failure_detection()
{
    QVERIFY(Asc::isAuthFailure(401, QString()));
    QVERIFY(Asc::isAuthFailure(500, QString::fromUtf8("请先登录")));
    QVERIFY(Asc::isAuthFailure(200, QString::fromUtf8("登录已过期")));
    QVERIFY(!Asc::isAuthFailure(500, QString::fromUtf8("题目不存在")));
    QVERIFY(!Asc::isAuthFailure(200, QStringLiteral("success")));
}

void TestAisichuang::tolerant_field_readers()
{
    QJsonObject row = jsonOf(R"({"a":"text","b":42,"c":"","d":true,"e":null})");
    QCOMPARE(Asc::strOf(row, {"a"}), QStringLiteral("text"));
    // Numbers are stringified because ids come back as numbers or strings.
    QCOMPARE(Asc::strOf(row, {"b"}), QStringLiteral("42"));
    QVERIFY(Asc::strOf(row, {"c"}).isEmpty());
    QCOMPARE(Asc::strOf(row, {"missing", "b"}), QStringLiteral("42"));
    QCOMPARE(Asc::numOf(row, {"a"}), qlonglong(0));
    QCOMPARE(Asc::numOf(row, {"b"}, -1), qlonglong(42));
    QCOMPARE(Asc::numOf(row, {"nope"}, 7), qlonglong(7));
    QVERIFY(Asc::boolOf(row, {"d"}));
    QVERIFY(!Asc::boolOf(row, {"nope"}, false));

    QJsonObject numericStrings = jsonOf(R"({"id":"123"})");
    QCOMPARE(Asc::numOf(numericStrings, {"id"}), qlonglong(123));

    // Either a bare array or a paged object is accepted.
    QCOMPARE(Asc::arrayOf(bareArray("[1,2,3]")).count(), 3);
    QCOMPARE(Asc::arrayOf(QJsonValue(jsonOf(R"({"records":[1,2,3]})"))).count(), 3);
    QCOMPARE(Asc::arrayOf(QJsonValue(jsonOf(R"({"list":[1]})"))).count(), 1);
    QCOMPARE(Asc::arrayOf(QJsonValue(jsonOf(R"({"rows":[1,2]})"))).count(), 2);
    QVERIFY(Asc::arrayOf(QJsonValue(jsonOf(R"({"nothing":1})"))).isEmpty());
    QVERIFY(Asc::arrayOf(QJsonValue()).isEmpty());
}

void TestAisichuang::normalize_output_is_oj_style()
{
    QCOMPARE(Asc::normalizeOutput(QString::fromUtf8("1 2\r\n3 4\r\n")),
             QStringLiteral("1 2\n3 4"));
    // Trailing blanks on a line and trailing blank lines are noise.
    QCOMPARE(Asc::normalizeOutput(QStringLiteral("42   \n\n\n")), QStringLiteral("42"));
    QCOMPARE(Asc::normalizeOutput(QStringLiteral("a\t\nb")), QStringLiteral("a\nb"));
    // Interior blank lines stay: they may be part of the expected output.
    QCOMPARE(Asc::normalizeOutput(QStringLiteral("a\n\nb")), QStringLiteral("a\n\nb"));
    QVERIFY(Asc::normalizeOutput(QString()).isEmpty());
}

void TestAisichuang::verdict_normalization()
{
    QCOMPARE(Asc::normalizeVerdict(QStringLiteral("AC")), QStringLiteral("AC"));
    QCOMPARE(Asc::normalizeVerdict(QStringLiteral("Accepted")), QStringLiteral("AC"));
    QCOMPARE(Asc::normalizeVerdict(QStringLiteral("OK")), QStringLiteral("AC"));
    QCOMPARE(Asc::normalizeVerdict(QStringLiteral("Wrong Answer")), QStringLiteral("WA"));
    QCOMPARE(Asc::normalizeVerdict(QStringLiteral("TLE (2001ms)")), QStringLiteral("TLE"));
    QCOMPARE(Asc::normalizeVerdict(QStringLiteral("Memory Limit Exceeded")), QStringLiteral("MLE"));
    QCOMPARE(Asc::normalizeVerdict(QStringLiteral("Runtime Error")), QStringLiteral("RE"));
    QCOMPARE(Asc::normalizeVerdict(QStringLiteral("Compile Error")), QStringLiteral("CE"));
    QCOMPARE(Asc::normalizeVerdict(QString::fromUtf8("答案错误")), QStringLiteral("WA"));
    QCOMPARE(Asc::normalizeVerdict(QString::fromUtf8("时间超限")), QStringLiteral("TLE"));
    QCOMPARE(Asc::normalizeVerdict(QString::fromUtf8("编译失败")), QStringLiteral("CE"));
    QCOMPARE(Asc::normalizeVerdict(QString::fromUtf8("通过")), QStringLiteral("AC"));
    QCOMPARE(Asc::normalizeVerdict(QStringLiteral("something new")), QStringLiteral("?"));
    QCOMPARE(Asc::normalizeVerdict(QString()), QStringLiteral("?"));

    // The field the site uses for the verdict may be spelled several ways.
    QJsonObject record = jsonOf(R"({"substatic":"答案正确","subIsPass":1})");
    QCOMPARE(Asc::verdictOfRecord(record), QStringLiteral("AC"));
}

void TestAisichuang::submission_finality_rule()
{
    // subIsPass 7 means the judge has not finished; the record may otherwise
    // look "done" while carrying no verdict yet, which reads as a false WA.
    AscSubmissionResult judging = Asc::submissionFrom(QJsonValue(jsonOf(
            R"({"subIsPass":7,"subStatic":"","id":0})")));
    QVERIFY(judging.judging);
    QVERIFY(!Asc::isFinalResult(judging));

    AscSubmissionResult notStarted = Asc::submissionFrom(QJsonValue(jsonOf(
            R"({"subIsPass":0,"subStatic":"","id":0})")));
    QVERIFY(!notStarted.judging);
    QVERIFY(!Asc::isFinalResult(notStarted));

    AscSubmissionResult finished = Asc::submissionFrom(QJsonValue(jsonOf(
            R"({"subIsPass":0,"subStatic":"","id":98765})")));
    QVERIFY(Asc::isFinalResult(finished));

    AscSubmissionResult withText = Asc::submissionFrom(QJsonValue(jsonOf(
            QString::fromUtf8(R"({"subIsPass":1,"id":0,"subStatic":"%1","subScore":100,"timeCost":15,"memoryCost":4096})")
            .arg(QString::fromUtf8("答案正确")))));
    QVERIFY(Asc::isFinalResult(withText));
    QCOMPARE(withText.verdict, QStringLiteral("AC"));
    QCOMPARE(withText.timeMs, qlonglong(15));
    QCOMPARE(withText.memoryBytes, qlonglong(4096));
    QVERIFY(withText.passed());

    // The snake_case spelling of the cost fields also shows up.
    AscSubmissionResult snake = Asc::submissionFrom(QJsonValue(jsonOf(
            R"({"subIsPass":0,"id":5,"subStatic":"Wrong Answer","time_cost":30,"memory_cost":1024})")));
    QCOMPARE(snake.timeMs, qlonglong(30));
    QCOMPARE(snake.memoryBytes, qlonglong(1024));
}

void TestAisichuang::submission_id_lives_in_msg()
{
    // data is null and msg carries the id; a reader of data alone gets nothing.
    QCOMPARE(Asc::submissionIdFrom(QJsonValue(), QStringLiteral("123456")), QStringLiteral("123456"));
    QCOMPARE(Asc::submissionIdFrom(QJsonValue(987.0), QStringLiteral("ignored")), QStringLiteral("987"));
    QCOMPARE(Asc::submissionIdFrom(QJsonValue(QStringLiteral("42")), QString()), QStringLiteral("42"));
    QVERIFY(Asc::submissionIdFrom(QJsonValue(), QString()).isEmpty());
}

void TestAisichuang::task_rows_use_lowercase_ids()
{
    QJsonValue tests = QJsonValue(jsonOf(R"({"total":2,"list":[
        {"testid":321,"testName":"链表","createTime":"2026-10-01","endTime":"2026-10-02","sum":4,"isSubmit":1},
        {"testId":654,"testName":"二分","sum":2,"isSubmit":0}
    ]})"));
    QVector<AscTaskInfo> loaded = Asc::tasksFrom(tests, AscProblemSource::Test);
    QCOMPARE(loaded.count(), 2);
    // "testid" all lowercase is the usual spelling.
    QCOMPARE(loaded[0].taskId, QStringLiteral("321"));
    QCOMPARE(loaded[0].taskName, QString::fromUtf8("链表"));
    QCOMPARE(loaded[0].problemCount, 4);
    QVERIFY(loaded[0].submitted);
    // The camelCase fallback has to work too.
    QCOMPARE(loaded[1].taskId, QStringLiteral("654"));
    QVERIFY(!loaded[1].submitted);

    QJsonValue works = QJsonValue(jsonOf(R"({"total":1,"list":[
        {"workid":777,"workName":"树上问题","sum":3,"isSubmit":0}
    ]})"));
    QVector<AscTaskInfo> homework = Asc::tasksFrom(works, AscProblemSource::Work);
    QCOMPARE(homework.count(), 1);
    QCOMPARE(homework[0].taskId, QStringLiteral("777"));
    QVERIFY(homework[0].isWork());
}

void TestAisichuang::task_problems_filter_and_done_flag()
{
    // The task problem list is a bare array, and it mixes question types:
    // only quesTypeId 5 is a programming problem.
    QJsonValue data = bareArray(R"([
        {"tkId":1,"displayId":"A1131","title":"sort","quesTypeId":5,"isDo":1,"records":3},
        {"tkId":2,"displayId":"A1132","title":"quiz","quesTypeId":2,"isDo":0},
        {"tkId":3,"displayId":"A1133","title":"tree","quesTypeId":5,"isDo":0}
    ])");
    QVector<AscTaskProblem> tests = Asc::taskProblemsFrom(data, AscProblemSource::Test);
    QCOMPARE(tests.count(), 2);
    QCOMPARE(tests[0].displayId, QStringLiteral("A1131"));
    QVERIFY(tests[0].done);
    QCOMPARE(tests[0].records, 3);
    QVERIFY(!tests[1].done);

    // Homework spells the same flag is_do instead of isDo.
    QJsonValue workData = bareArray(R"([
        {"tkId":9,"displayId":"B2404","title":"dp","quesTypeId":5,"is_do":1}
    ])");
    QVector<AscTaskProblem> works = Asc::taskProblemsFrom(workData, AscProblemSource::Work);
    QCOMPARE(works.count(), 1);
    QVERIFY(works[0].done);
}

void TestAisichuang::class_rows_either_array_or_records()
{
    QVector<AscClassInfo> fromRecords = Asc::classesFrom(QJsonValue(jsonOf(R"({"records":[
        {"id":12,"className":"CS2026秋季","classroomName":"S01"}
    ]})")));
    QCOMPARE(fromRecords.count(), 1);
    QCOMPARE(fromRecords[0].classId, QStringLiteral("12"));
    QCOMPARE(fromRecords[0].className, QString::fromUtf8("CS2026秋季"));

    QVector<AscClassInfo> fromArray = Asc::classesFrom(bareArray(R"([
        {"id":13,"className":"CS2025春季"}
    ])"));
    QCOMPARE(fromArray.count(), 1);
    QCOMPARE(fromArray[0].classId, QStringLiteral("13"));

    // A row without an id cannot be used for anything.
    QVERIFY(Asc::classesFrom(QJsonValue(jsonOf(R"({"records":[{"className":"x"}]})"))).isEmpty());
}

void TestAisichuang::statement_from_oj_dto()
{
    QJsonObject dto = jsonOf(R"({
        "_id":"A1131",
        "title":"排序",
        "description":"给一个数组排序",
        "input_description":"第一行 n",
        "output_description":"一行答案",
        "hint":"注意负数",
        "time_limit":1000,
        "memory_limit":128,
        "samples":[{"input":"3\n1 2 3\n","output":"1 2 3\n"},{"input":"2\n","output":""}]
    })");
    AscStatement statement = Asc::statementFromOjDto(dto, QStringLiteral("A1131"), QString());
    QCOMPARE(statement.displayId, QStringLiteral("A1131"));
    QCOMPARE(statement.title, QString::fromUtf8("排序"));
    QCOMPARE(statement.description, QString::fromUtf8("给一个数组排序"));
    QCOMPARE(statement.inputDescription, QString::fromUtf8("第一行 n"));
    QCOMPARE(statement.outputDescription, QString::fromUtf8("一行答案"));
    QCOMPARE(statement.timeLimitMs, qlonglong(1000));
    QCOMPARE(statement.memoryLimitMb, qlonglong(128));
    // A sample with an input but no expected output is still a sample.
    QCOMPARE(statement.samples.count(), 2);
    QCOMPARE(statement.samples[0].input, QStringLiteral("3\n1 2 3\n"));
    QVERIFY(statement.rawMarkdown.isEmpty());

    // camelCase spellings of the limits are also seen.
    QJsonObject alt = jsonOf(R"({"_id":"X1","title":"t","timeLimit":2000,"memoryLimit":256,
                                "inputFormat":"in","outputFormat":"out"})");
    AscStatement other = Asc::statementFromOjDto(alt, QStringLiteral("X1"), QStringLiteral("fallback"));
    QCOMPARE(other.timeLimitMs, qlonglong(2000));
    QCOMPARE(other.memoryLimitMb, qlonglong(256));
    QCOMPARE(other.inputDescription, QStringLiteral("in"));
    QCOMPARE(other.outputDescription, QStringLiteral("out"));

    // Empty payloads must not produce garbage.
    AscStatement empty = Asc::statementFromOjDto(QJsonObject(), QStringLiteral("A9"), QStringLiteral("Nine"));
    QCOMPARE(empty.displayId, QStringLiteral("A9"));
    QCOMPARE(empty.title, QStringLiteral("Nine"));
    QCOMPARE(empty.timeLimitMs, qlonglong(0));
    QVERIFY(empty.samples.isEmpty());
}

void TestAisichuang::statement_from_detail_and_markdown()
{
    QJsonObject dto = jsonOf(R"({
        "displayId":"B2404",
        "titleName":"树上DP",
        "titleDescribe":"描述",
        "inputForm":"输入格式",
        "outputForm":"输出格式",
        "titlePrompt":"提示",
        "titleBackground":"背景",
        "timeLimit":1000,
        "memoryLimit":256,
        "all":"# B2404 树上DP\n\n正文",
        "samples":[{"sampleInput":"1","sampleOutput":"2"}]
    })");
    AscStatement statement = Asc::statementFromDetail(dto, QStringLiteral("B2404"), QString());
    QCOMPARE(statement.displayId, QStringLiteral("B2404"));
    QCOMPARE(statement.title, QString::fromUtf8("树上DP"));
    QCOMPARE(statement.description, QString::fromUtf8("描述"));
    QCOMPARE(statement.inputDescription, QString::fromUtf8("输入格式"));
    QCOMPARE(statement.hint, QString::fromUtf8("背景"));
    QCOMPARE(statement.prompt, QString::fromUtf8("提示"));
    QCOMPARE(statement.rawMarkdown, QString::fromUtf8("# B2404 树上DP\n\n正文"));
    QCOMPARE(statement.samples.count(), 1);
    QCOMPARE(statement.samples[0].input, QStringLiteral("1"));

    // Some details carry a single sample as two top level fields.
    QJsonObject single = jsonOf(R"({"displayId":"C1","input":"a","output":"b"})");
    AscStatement oneSample = Asc::statementFromDetail(single, QStringLiteral("C1"), QString());
    QCOMPARE(oneSample.samples.count(), 1);
    QCOMPARE(oneSample.samples[0].output, QStringLiteral("b"));
}

void TestAisichuang::samples_shape_variants()
{
    QJsonArray samples = arrayOf(R"([
        {"input":"1","output":"2"},
        {"sampleInput":"3","sampleOutput":"4"},
        {"in":"5","out":"6"},
        {"inputSample":"7","outputSample":"8"},
        {"input":"","output":""},
        {"input":"9"},
        {}
    ])");
    QVector<AscSample> picked = Asc::pickSamples(samples);
    QCOMPARE(picked.count(), 5);
    QCOMPARE(picked[1].input, QStringLiteral("3"));
    QCOMPARE(picked[2].output, QStringLiteral("6"));
    QCOMPARE(picked[3].input, QStringLiteral("7"));
    // An input without an expected output is still worth running.
    QCOMPARE(picked[4].input, QStringLiteral("9"));
    QVERIFY(picked[4].output.isEmpty());

    // Composed markdown is what the saved .md falls back to.
    AscStatement statement;
    statement.displayId = QStringLiteral("A1");
    statement.title = QStringLiteral("Demo");
    statement.description = QStringLiteral("desc");
    statement.timeLimitMs = 1000;
    statement.memoryLimitMb = 128;
    statement.samples.append(AscSample{QStringLiteral("in"), QStringLiteral("out")});
    QString markdown = Asc::statementToMarkdown(statement);
    QVERIFY(markdown.contains(QStringLiteral("A1 Demo")));
    QVERIFY(markdown.contains(QStringLiteral("desc")));
    QVERIFY(markdown.contains(QStringLiteral("```")));
    QVERIFY(markdown.contains(QStringLiteral("in")));
}

void TestAisichuang::problem_id_shape_and_masking()
{
    QVERIFY(Asc::looksLikeProblemId(QStringLiteral("A1131")));
    QVERIFY(Asc::looksLikeProblemId(QStringLiteral("G120260901")));
    QVERIFY(Asc::looksLikeProblemId(QStringLiteral("PKJ02h05")));
    QVERIFY(!Asc::looksLikeProblemId(QStringLiteral("main")));
    QVERIFY(!Asc::looksLikeProblemId(QStringLiteral("1A")));
    QVERIFY(!Asc::looksLikeProblemId(QString()));

    QCOMPARE(Asc::stemOf(QStringLiteral("/tmp/x/A1131.cpp")), QStringLiteral("A1131"));
    QCOMPARE(Asc::maskAccount(QStringLiteral("13800001234")), QStringLiteral("138****34"));
    QCOMPARE(Asc::maskAccount(QStringLiteral("ab")), QStringLiteral("ab"));
    QCOMPARE(Asc::formatBytes(512), QStringLiteral("512 B"));
    QCOMPARE(Asc::formatBytes(2048), QStringLiteral("2.0 KB"));
    QCOMPARE(Asc::formatBytes(qlonglong(5) * 1024 * 1024), QStringLiteral("5.0 MB"));
}

void TestAisichuang::problem_files_and_cases_are_written()
{
    // This is the step that turns a downloaded problem into the IDE's own
    // problem-set entry, which the existing "run all cases" machinery judges.
    QTemporaryDir dir;
    QVERIFY(dir.isValid());
    const QString folder = dir.path();

    AscStatement statement;
    statement.problemId = QStringLiteral("555");
    statement.displayId = QStringLiteral("A1131");
    statement.title = QStringLiteral("排序");
    statement.description = QStringLiteral("给数组排序");
    statement.inputDescription = QStringLiteral("第一行 n");
    statement.timeLimitMs = 0;          // the site omits it some times
    statement.memoryLimitMb = 128;
    AscSample first;
    first.input = QStringLiteral("3\r\n1 2 3\r\n");
    first.output = QStringLiteral("1 2 3   \r\n\r\n");
    AscSample second;
    second.input = QStringLiteral("1\n5\n");
    second.output = QStringLiteral("5");
    statement.samples << first << second;

    AscImportContext context;
    context.source = AscProblemSource::Test;
    context.taskId = QStringLiteral("30582");
    context.taskName = QStringLiteral("入门基础");

    const QString templateText = QStringLiteral("#include <bits/stdc++.h>\nint main() { return 0; }\n");
    AscImportedProblem imported =
            AscProblemImporter::writeProblem(statement, context, folder, templateText, true);

    QVERIFY(fileExists(imported.sourcePath));
    QVERIFY(fileExists(getFilePath(folder, QStringLiteral("A1131_1.in"))));
    QVERIFY(fileExists(getFilePath(folder, QStringLiteral("A1131_1.out"))));
    QVERIFY(fileExists(getFilePath(folder, QStringLiteral("A1131_2.in"))));
    QVERIFY(fileExists(getFilePath(folder, QStringLiteral("A1131_2.out"))));
    QVERIFY(fileExists(getFilePath(folder, QStringLiteral("A1131.md"))));
    QVERIFY(readFileToByteArray(imported.sourcePath).contains("bits/stdc++"));

    // Line endings normalised, trailing blanks of a line and the trailing
    // blank lines of the file removed, and one final newline kept.
    QCOMPARE(QString::fromUtf8(readFileToByteArray(getFilePath(folder, QStringLiteral("A1131_1.in")))),
             QStringLiteral("3\n1 2 3\n"));
    QCOMPARE(QString::fromUtf8(readFileToByteArray(getFilePath(folder, QStringLiteral("A1131_1.out")))),
             QStringLiteral("1 2 3\n"));
    QCOMPARE(QString::fromUtf8(readFileToByteArray(getFilePath(folder, QStringLiteral("A1131_2.out")))),
             QStringLiteral("5\n"));

    QVERIFY(imported.problem);
    QCOMPARE(imported.problem->name(), QStringLiteral("A1131 排序"));
    QCOMPARE(imported.problem->cases().count(), 2);
    QCOMPARE(imported.problem->answerProgram(), imported.sourcePath);
    // A missing limit from the site must still end a hung program.
    QCOMPARE(qlonglong(imported.problem->getTimeLimitInMilliseconds()), qlonglong(2000));
    QCOMPARE(qlonglong(imported.problem->getMemoryLimitInBytes()), qlonglong(128) * 1024 * 1024);

    const POJProblemCase& caseOne = imported.problem->cases().first();
    QCOMPARE(caseOne->name(), QStringLiteral("Sample 1"));
    QVERIFY(caseOne->inputFileName().endsWith(QStringLiteral("A1131_1.in")));
    QVERIFY(caseOne->expectedOutputFileName().endsWith(QStringLiteral("A1131_1.out")));
    QCOMPARE(caseOne->testState, ProblemCaseTestState::NotTested);
    // The inline copy stays untouched so the panel still shows what the site said.
    QVERIFY(caseOne->expected().contains(QStringLiteral("1 2 3   ")));

    // An answer file the student already wrote must never be replaced.
    const QString marker = QStringLiteral("// my own work\n");
    stringToFile(marker, imported.sourcePath);
    const QString mdPath = getFilePath(folder, QStringLiteral("A1131.md"));
    const QString mdMarker = QStringLiteral("kept\n");
    stringToFile(mdMarker, mdPath);
    AscImportedProblem again =
            AscProblemImporter::writeProblem(statement, context, folder, templateText, false);
    QCOMPARE(QString::fromUtf8(readFileToByteArray(again.sourcePath)), marker);
    // With the markdown option off, the existing statement file is left alone.
    QCOMPARE(QString::fromUtf8(readFileToByteArray(mdPath)), mdMarker);
    // Re-importing replaces the case list rather than doubling it.
    QCOMPARE(again.problem->cases().count(), 2);

    // Statement markdown is only built when the site gave none.
    AscStatement withMarkdown = statement;
    withMarkdown.rawMarkdown = QStringLiteral("# 站点自己的题面");
    AscImportedProblem third =
            AscProblemImporter::writeProblem(withMarkdown, context, folder, templateText, true);
    QVERIFY(third.problem);
    QVERIFY(QString::fromUtf8(readFileToByteArray(mdPath)).contains(QStringLiteral("# 站点自己的题面")));
}

QString TestAisichuang::runStep(AscClient& client, const std::function<void(DoneFn, FailFn)>& call)
{
    // The handlers are taken by value on purpose: the client keeps the
    // std::function it was handed and calls it when the reply lands, long
    // after `call` returns, so a reference to a temporary would dangle.
    QEventLoop loop;
    bool finished = false;
    QString error;
    QTimer watchdog;
    watchdog.setSingleShot(true);
    QObject::connect(&watchdog, &QTimer::timeout, &loop, [&]() {
        if (!finished)
            error = QStringLiteral("timed out after 60s");
        loop.quit();
    });
    watchdog.start(60000);
    call(DoneFn([&]() {
             finished = true;
             watchdog.stop();
             loop.quit();
         }),
         FailFn([&](const QString& message) {
             finished = true;
             error = message;
             watchdog.stop();
             loop.quit();
         }));
    loop.exec();
    // Nothing may fire once this frame is gone.
    client.cancelAll();
    return error;
}

bool TestAisichuang::liveLogin(AscClient& client, AscSession& session, QString* error)
{
    *error = runStep(client, [&](DoneFn done, FailFn fail) {
        client.login(mPhone, mPassword,
                     [&session, done](const AscSession& value) {
                         session = value;
                         done();
                     },
                     [fail](const QString& message) {
                         fail(message);
                     });
    });
    return error->isEmpty() && session.isValid();
}

void TestAisichuang::live_login_and_statement()
{
    if (!mLiveEnabled)
        QSKIP("set ASC_LIVE=1 with ASC_PHONE / ASC_PASSWORD to test against the real site");
    if (mPhone.isEmpty() || mPassword.isEmpty())
        QSKIP("ASC_PHONE / ASC_PASSWORD are not set");

    qInfo() << "ssl support:" << QSslSocket::supportsSsl()
            << QSslSocket::sslLibraryBuildVersionString();
    QVERIFY2(QSslSocket::supportsSsl(), "Qt cannot do TLS here, so https would never work");

    AscClient client;
    client.setBaseUrl(QStringLiteral("https://www.aisichuang.net/tiku"));
    client.setThrottleMs(200);

    // 1. Login: the CSRF bootstrap plus the plaintext password exchange.
    AscSession session;
    QString error;
    QVERIFY2(liveLogin(client, session, &error), qPrintable(error));
    QVERIFY(!session.userId.isEmpty());
    QVERIFY(!session.role.isEmpty());
    qInfo() << "logged in as role" << session.role << "user" << session.userId;

    // 2. The probe doubles as the language list used for submissions.
    QStringList languages;
    error = runStep(client, [&](DoneFn done, FailFn fail) {
        client.probeSession([&languages, done](const QStringList& value) {
                                languages = value;
                                done();
                            },
                            [fail](const QString& message) { fail(message); });
    });
    QVERIFY2(error.isEmpty(), qPrintable(error));
    QVERIFY(!languages.isEmpty());
    qInfo() << "languages:" << languages;

    // 3. A bank search must return rows with both an internal id and a display id.
    QVector<AscProblemRef> hits;
    qlonglong total = 0;
    error = runStep(client, [&](DoneFn done, FailFn fail) {
        client.searchProblems(QStringLiteral("A1"), 1,
                              [&hits, &total, done](const QVector<AscProblemRef>& problems,
                                                    qlonglong count) {
                                  hits = problems;
                                  total = count;
                                  done();
                              },
                              [fail](const QString& message) { fail(message); });
    });
    QVERIFY2(error.isEmpty(), qPrintable(error));
    qInfo() << "bank search total" << total << "shown" << hits.count();
    if (hits.isEmpty())
        return;
    for (const AscProblemRef& hit : hits) {
        QVERIFY(!hit.displayId.isEmpty());
        QVERIFY(!hit.problemId.isEmpty());
    }

    // 4. Statement and samples of the first hit, through the OjDto/detail chain.
    const QString displayId = hits.first().displayId;
    AscStatement statement;
    error = runStep(client, [&](DoneFn done, FailFn fail) {
        client.fetchStatement(displayId, hits.first().title,
                              [&statement, done](const AscStatement& value) {
                                  statement = value;
                                  done();
                              },
                              [fail](const QString& message) { fail(message); });
    });
    QVERIFY2(error.isEmpty(), qPrintable(error));
    QCOMPARE(statement.displayId, displayId);
    qInfo() << "statement:" << statement.title
            << "| samples:" << statement.samples.count()
            << "| time:" << statement.timeLimitMs << "ms"
            << "| memory:" << statement.memoryLimitMb << "MB"
            << "| markdown chars:" << statement.rawMarkdown.length();
    QVERIFY(statement.timeLimitMs > 0);
}

void TestAisichuang::live_class_and_task_lists()
{
    if (!mLiveEnabled)
        QSKIP("set ASC_LIVE=1 with ASC_PHONE / ASC_PASSWORD to test against the real site");
    if (mPhone.isEmpty() || mPassword.isEmpty())
        QSKIP("ASC_PHONE / ASC_PASSWORD are not set");

    AscClient client;
    client.setBaseUrl(QStringLiteral("https://www.aisichuang.net/tiku"));
    client.setThrottleMs(200);

    AscSession session;
    QString error;
    QVERIFY2(liveLogin(client, session, &error), qPrintable(error));

    // 1. The class list is teacher/admin only; a student account getting an
    // error here is the site's behaviour, not a bug in this client.
    QVector<AscClassInfo> classes;
    error = runStep(client, [&](DoneFn done, FailFn fail) {
        client.listClasses([&classes, done](const QVector<AscClassInfo>& value) {
                               classes = value;
                               done();
                           },
                           [fail](const QString& message) {
                               qInfo() << "class list unavailable:" << message;
                               fail(message);
                           });
    });
    if (!error.isEmpty())
        return;
    qInfo() << "classes:" << classes.count();
    if (classes.isEmpty())
        return;
    for (const AscClassInfo& info : classes)
        QVERIFY(!info.classId.isEmpty());

    // 2. Walk a few classes until one has content: finished classes
    // legitimately return nothing, so an empty list is not a failure.
    QString classId;
    QString taskId;
    QVector<AscTaskInfo> tasks;
    AscProblemSource taskSource = AscProblemSource::Test;
    const int probeCount = qMin(8, classes.count());
    for (int i = 0; i < probeCount && taskId.isEmpty(); ++i) {
        for (AscProblemSource source : {AscProblemSource::Test, AscProblemSource::Work}) {
            QVector<AscTaskInfo> found;
            error = runStep(client, [&](DoneFn done, FailFn fail) {
                client.listTasks(source, classes[i].classId,
                                 [&found, done](const QVector<AscTaskInfo>& value) {
                                     found = value;
                                     done();
                                 },
                                 [fail](const QString& message) { fail(message); });
            });
            QVERIFY2(error.isEmpty(), qPrintable(error));
            if (found.isEmpty())
                continue;
            classId = classes[i].classId;
            tasks = found;
            taskSource = source;
            taskId = found.first().taskId;
            qInfo() << "class" << classId << "has" << found.count()
                    << (source == AscProblemSource::Work ? "homework entries" : "tests")
                    << "; first is" << found.first().taskName;
            break;
        }
    }
    if (taskId.isEmpty()) {
        qInfo() << "none of the first" << probeCount << "classes has tests or homework";
        return;
    }

    // Row shape: the ids the panel keys on must be numeric strings.
    for (const AscTaskInfo& task : tasks) {
        bool ok = false;
        task.taskId.toLongLong(&ok);
        QVERIFY2(ok, qPrintable(QStringLiteral("task id is not numeric: %1").arg(task.taskId)));
        QVERIFY(!task.taskName.isEmpty());
    }

    // 3. Problems of that task: only programming rows survive the filter, and
    // each must resolve to a bank problem so its samples can be downloaded.
    QVector<AscTaskProblem> problems;
    error = runStep(client, [&](DoneFn done, FailFn fail) {
        client.listTaskProblems(taskSource, taskId,
                                [&problems, done](const QVector<AscTaskProblem>& value) {
                                    problems = value;
                                    done();
                                },
                                [fail](const QString& message) { fail(message); });
    });
    QVERIFY2(error.isEmpty(), qPrintable(error));
    qInfo() << "task" << taskId << "programming problems:" << problems.count()
            << "| done:" << std::count_if(problems.constBegin(), problems.constEnd(),
                                          [](const AscTaskProblem& p) { return p.done; });
    QVERIFY(!problems.isEmpty());
    for (const AscTaskProblem& problem : problems)
        QVERIFY(!problem.displayId.isEmpty());

    const QString displayId = problems.first().displayId;
    QVector<AscProblemRef> hits;
    error = runStep(client, [&](DoneFn done, FailFn fail) {
        client.searchProblems(displayId, 1,
                              [&hits, done](const QVector<AscProblemRef>& value, qlonglong) {
                                  hits = value;
                                  done();
                              },
                              [fail](const QString& message) { fail(message); });
    });
    QVERIFY2(error.isEmpty(), qPrintable(error));
    bool matched = false;
    for (const AscProblemRef& hit : hits) {
        if (hit.displayId.compare(displayId, Qt::CaseInsensitive) == 0)
            matched = true;
    }
    QVERIFY2(matched, qPrintable(QStringLiteral("task problem %1 is not in the bank").arg(displayId)));

    // 4. And its statement with samples is reachable through the same chain the
    // panel uses when creating the local files.
    AscStatement statement;
    error = runStep(client, [&](DoneFn done, FailFn fail) {
        client.fetchStatement(displayId, problems.first().title,
                              [&statement, done](const AscStatement& value) {
                                  statement = value;
                                  done();
                              },
                              [fail](const QString& message) { fail(message); });
    });
    QVERIFY2(error.isEmpty(), qPrintable(error));
    QCOMPARE(statement.displayId, displayId);
    qInfo() << "task problem" << displayId << statement.title
            << "| samples:" << statement.samples.count()
            << "| time:" << statement.timeLimitMs << "ms";
}
