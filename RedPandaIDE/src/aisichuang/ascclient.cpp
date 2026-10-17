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
#include "ascclient.h"

#include "ascenvelope.h"
#include "ascstatement.h"

#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonObject>
#include <QNetworkAccessManager>
#include <QNetworkReply>
#include <QNetworkRequest>
#include <QTimer>
#include <QUrlQuery>

/**
 * The site checks neither the user agent nor the referer loosely: calls that
 * look like a generic HTTP library get refused, so these stay fixed.
 */
static const char ASC_USER_AGENT[] =
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 "
        "(KHTML, like Gecko) Chrome/128.0 Safari/537.36";
static const char ASC_REFERER[] = "https://www.aisichuang.net/";

static const int ASC_REQUEST_TIMEOUT_MS = 20000;
static const int ASC_TASK_PAGE_SIZE = 50;
/** The site happily serves hundreds of rows; a teacher never needs that many. */
static const int ASC_TASK_PAGE_CAP = 5;

/** Ids are numeric on the wire; keep them numbers when they parse as such. */
static QJsonValue idValue(const QString& id)
{
    if (id.trimmed().isEmpty())
        return QJsonValue(double(0));
    bool ok = false;
    qlonglong number = id.toLongLong(&ok);
    if (ok)
        return QJsonValue(double(number));
    return QJsonValue(id);
}

AscClient::AscClient(QObject* parent):
    QObject(parent),
    mNetwork(new QNetworkAccessManager(this)),
    mPumpTimer(new QTimer(this)),
    mCurrentReply(nullptr),
    mExecuting(false),
    mGeneration(0),
    mThrottleMs(150)
{
    mPumpTimer->setSingleShot(true);
    connect(mPumpTimer, &QTimer::timeout, this, [this]() {
        pump();
    });
}

AscClient::~AscClient()
{
    cancelAll();
}

void AscClient::setBaseUrl(const QString& baseUrl)
{
    mBaseUrl = baseUrl;
    while (mBaseUrl.endsWith(QLatin1Char('/')))
        mBaseUrl.chop(1);
    mSession.baseUrl = mBaseUrl;
}

QString AscClient::baseUrl() const
{
    return mBaseUrl;
}

void AscClient::setSession(const AscSession& session)
{
    mSession = session;
    if (mSession.baseUrl.isEmpty())
        mSession.baseUrl = mBaseUrl;
}

const AscSession& AscClient::session() const
{
    return mSession;
}

bool AscClient::isLoggedIn() const
{
    return mSession.isValid();
}

void AscClient::setThrottleMs(int ms)
{
    mThrottleMs = qMax(0, ms);
}

void AscClient::cancelAll()
{
    mGeneration++;
    mQueue.clear();
    mExecuting = false;
    mPumpTimer->stop();
    if (mCurrentReply) {
        QNetworkReply* reply = mCurrentReply;
        mCurrentReply = nullptr;
        reply->abort();
        reply->deleteLater();
    }
}

void AscClient::enqueue(const Job& job)
{
    mQueue.enqueue(job);
    pump();
}

void AscClient::pump()
{
    if (mExecuting || mQueue.isEmpty())
        return;
    mPumpTimer->stop();
    mExecuting = true;
    Job job = mQueue.dequeue();
    job();
    // A job that fails before starting a request must not stall the queue;
    // every such path calls releasePump() itself.
}

void AscClient::releasePump()
{
    mExecuting = false;
    mCurrentReply = nullptr;
    if (!mQueue.isEmpty())
        mPumpTimer->start(mThrottleMs);
}

QString AscClient::describeHttpFailure(QNetworkReply* reply)
{
    QVariant status = reply->attribute(QNetworkRequest::HttpStatusCodeAttribute);
    if (status.isValid())
        return QObject::tr("The site returned HTTP %1.").arg(status.toInt());
    return QObject::tr("Network error: %1").arg(reply->errorString());
}

void AscClient::request(const QString& method, const QString& path, const QJsonObject& body,
                        bool withCsrf, const DataHandler& onOk, const ErrorHandler& onErr,
                        const QString& csrfOverride)
{
    enqueue([this, method, path, body, withCsrf, csrfOverride, onOk, onErr]() {
        startRequest(method, path, body, withCsrf, csrfOverride, onOk, onErr);
    });
}

QUrl AscClient::buildUrl(const QString& path) const
{
    QString base = mSession.baseUrl.isEmpty() ? mBaseUrl : mSession.baseUrl;
    return QUrl(base + path);
}

void AscClient::startRequest(const QString& method, const QString& path, const QJsonObject& body,
                             bool withCsrf, const QString& csrfOverride,
                             const DataHandler& onOk, const ErrorHandler& onErr)
{
    const int generation = mGeneration;

    QUrl target = buildUrl(path);
    if (!target.isValid()) {
        releasePump();
        onErr(tr("Invalid address: %1").arg(target.toString()));
        return;
    }

    QNetworkRequest netRequest(target);
    netRequest.setHeader(QNetworkRequest::UserAgentHeader, QString::fromLatin1(ASC_USER_AGENT));
    netRequest.setRawHeader("Referer", QByteArray(ASC_REFERER));
    // Qt's network stack does not decode gzip, so ask for an identity body.
    netRequest.setRawHeader("Accept-Encoding", "identity");
    netRequest.setAttribute(QNetworkRequest::CacheLoadControlAttribute, QNetworkRequest::AlwaysNetwork);
#if QT_VERSION >= QT_VERSION_CHECK(5, 15, 0)
    netRequest.setTransferTimeout(ASC_REQUEST_TIMEOUT_MS);
#endif

    if (mSession.isValid()) {
        netRequest.setRawHeader("satoken", mSession.satoken.toUtf8());
        netRequest.setRawHeader("sessionid", mSession.sessionId.toUtf8());
    }
    QString csrf = csrfOverride.isEmpty() ? mSession.csrftoken : csrfOverride;
    if (withCsrf && !csrf.isEmpty()) {
        netRequest.setRawHeader("X-CSRFToken", csrf.toUtf8());
        netRequest.setRawHeader("csrftoken", csrf.toUtf8());
    }

    QNetworkReply* reply = nullptr;
    if (method == QLatin1String("GET")) {
        reply = mNetwork->get(netRequest);
    } else {
        netRequest.setHeader(QNetworkRequest::ContentTypeHeader, QStringLiteral("application/json"));
        reply = mNetwork->post(netRequest, QJsonDocument(body).toJson(QJsonDocument::Compact));
    }
    mCurrentReply = reply;

    connect(reply, &QNetworkReply::finished, this, [this, reply, generation, onOk, onErr]() {
        reply->deleteLater();
        if (generation != mGeneration) {
            // cancelled: drop the result on the floor
            return;
        }
        releasePump();

        const QByteArray raw = reply->readAll();
        QJsonParseError parseError;
        QJsonDocument document = QJsonDocument::fromJson(raw, &parseError);
        if (parseError.error == QJsonParseError::NoError && document.isObject()) {
            QJsonObject envelope = document.object();
            qint64 code = -1;
            QString message;
            if (Asc::envelopeSuccess(envelope, &code, &message)) {
                onOk(envelope.value(QStringLiteral("data")), message);
                return;
            }
            if (Asc::isAuthFailure(code, message)) {
                emit authExpired();
                onErr(tr("The aisichuang session has expired. Please log in again."));
                return;
            }
            if (message.isEmpty())
                message = describeHttpFailure(reply);
            else
                message = tr("The site reported: %1 (code %2)").arg(message).arg(code);
            onErr(message);
            return;
        }

        // No parseable envelope: the site changed, or the request never worked.
        QString reason = describeHttpFailure(reply);
        if (raw.trimmed().isEmpty())
            reason += tr(" The response was not JSON, so the site may have changed.");
        onErr(reason);
    });
}

void AscClient::login(const QString& phone, const QString& password,
                      const std::function<void(const AscSession& session)>& onOk,
                      const ErrorHandler& onErr)
{
    // Step 1: bootstrap the CSRF token. A failure here is tolerated: the
    // login call is then sent without CSRF headers, which still works today.
    request(QStringLiteral("GET"), QStringLiteral("/api/profile"), QJsonObject(), false,
            [this, phone, password, onOk, onErr](const QJsonValue& data, const QString&) {
        QString csrf;
        if (data.isObject())
            csrf = Asc::strOf(data.toObject(), {QStringLiteral("csrftoken")});

        QJsonObject body;
        body.insert(QStringLiteral("phone"), phone);
        body.insert(QStringLiteral("password"), password);

        // Step 2: the real login. The password is sent as-is; the site does no
        // client side hashing, and this is exactly what its own front-end does.
        request(QStringLiteral("POST"), QStringLiteral("/login/loginByPassword"), body, true,
                [this, phone, onOk, onErr](const QJsonValue& loginData, const QString&) {
            if (!loginData.isObject()) {
                onErr(tr("Login failed: the site returned no session."));
                return;
            }
            QJsonObject obj = loginData.toObject();
            QString token;
            if (obj.value(QStringLiteral("tokenInfo")).isObject()) {
                token = Asc::strOf(obj.value(QStringLiteral("tokenInfo")).toObject(),
                                   {QStringLiteral("tokenValue")});
            }
            if (token.isEmpty())
                token = Asc::strOf(obj, {QStringLiteral("tokenValue"), QStringLiteral("token")});
            if (token.isEmpty()) {
                onErr(tr("Login failed: the site did not hand out a token."));
                return;
            }
            AscSession session;
            session.baseUrl = mBaseUrl;
            session.satoken = token;
            session.csrftoken = Asc::strOf(obj, {QStringLiteral("csrftoken")});
            session.sessionId = Asc::strOf(obj, {QStringLiteral("sessionid"), QStringLiteral("sessionId")});
            session.userId = Asc::strOf(obj, {QStringLiteral("id"), QStringLiteral("userId")});
            session.role = Asc::strOf(obj, {QStringLiteral("role")});
            if (session.role.isEmpty())
                session.role = QStringLiteral("student");
            session.phone = phone;
            mSession = session;
            onOk(session);
        }, onErr, csrf);
    }, onErr);
}

void AscClient::probeSession(const std::function<void(const QStringList& languages)>& onOk,
                             const ErrorHandler& onErr)
{
    request(QStringLiteral("GET"), QStringLiteral("/api/languages"), QJsonObject(), false,
            [onOk, onErr](const QJsonValue& data, const QString&) {
        if (!data.isArray()) {
            onErr(tr("The session probe returned no language list."));
            return;
        }
        QStringList languages;
        const QJsonArray array = data.toArray();
        for (const QJsonValue& value : array) {
            if (value.isObject()) {
                QString name = Asc::strOf(value.toObject(), {QStringLiteral("name"),
                                                              QStringLiteral("language"),
                                                              QStringLiteral("label"),
                                                              QStringLiteral("title")});
                if (!name.isEmpty())
                    languages << name;
            } else if (value.isString()) {
                languages << value.toString();
            }
        }
        onOk(languages);
    }, onErr);
}

void AscClient::searchProblems(const QString& keyword, int page,
                               const std::function<void(const QVector<AscProblemRef>& problems, qlonglong total)>& onOk,
                               const ErrorHandler& onErr)
{
    QJsonObject body;
    body.insert(QStringLiteral("page"), page);
    body.insert(QStringLiteral("pageSize"), 20);
    // The site searches both the display id and the title through "title".
    body.insert(QStringLiteral("title"), keyword);
    body.insert(QStringLiteral("role"), mSession.role.isEmpty() ? QStringLiteral("student") : mSession.role);
    body.insert(QStringLiteral("userId"), idValue(mSession.userId));
    body.insert(QStringLiteral("difficultyDenchmarking"), QJsonValue(QJsonValue::Null));
    body.insert(QStringLiteral("difficultyRating"), 0);
    body.insert(QStringLiteral("knowledgeId"), QString());
    body.insert(QStringLiteral("origin"), QString());
    body.insert(QStringLiteral("level"), 0);
    body.insert(QStringLiteral("ruleType"), QString());
    body.insert(QStringLiteral("course"), QString());

    request(QStringLiteral("POST"), QStringLiteral("/questionTk/getPage"), body, true,
            [onOk, onErr](const QJsonValue& data, const QString&) {
        if (!data.isObject()) {
            onOk(QVector<AscProblemRef>(), 0);
            return;
        }
        QJsonObject obj = data.toObject();
        qlonglong total = Asc::numOf(obj, {QStringLiteral("total")}, 0);
        onOk(Asc::problemRefsFrom(obj), total);
    }, onErr);
}

void AscClient::findProblem(const QString& displayId,
                            const std::function<void(const AscProblemRef& problem)>& onOk,
                            const ErrorHandler& onErr)
{
    searchProblems(displayId, 1, [displayId, onOk, onErr](const QVector<AscProblemRef>& problems, qlonglong) {
        // Never fall back to "the first hit": a wrong problem would be judged
        // against the wrong samples.
        for (const AscProblemRef& problem : problems) {
            if (problem.displayId.compare(displayId, Qt::CaseInsensitive) == 0) {
                onOk(problem);
                return;
            }
        }
        onErr(tr("Problem %1 was not found in the question bank.").arg(displayId));
    }, onErr);
}

void AscClient::fetchStatement(const QString& displayId, const QString& title,
                               const std::function<void(const AscStatement& statement)>& onOk,
                               const ErrorHandler& onErr)
{
    findProblem(displayId, [this, displayId, title, onOk, onErr](const AscProblemRef& problem) {
        QString path = QStringLiteral("/api/getProblemOjDto?problemId=") + problem.problemId;
        request(QStringLiteral("GET"), path, QJsonObject(), false,
                [this, problem, displayId, title, onOk, onErr](const QJsonValue& data, const QString&) {
            AscStatement statement;
            if (data.isObject()) {
                statement = Asc::statementFromOjDto(data.toObject(), displayId, title);
            }
            statement.problemId = problem.problemId;
            if (statement.displayId.isEmpty())
                statement.displayId = problem.displayId;
            if (statement.title.isEmpty())
                statement.title = problem.title;
            onOk(statement);
        },
                [this, problem, displayId, title, onOk, onErr](const QString& firstError) {
            // Some problems always fail here; the detail endpoint knows them.
            QJsonObject body;
            body.insert(QStringLiteral("questionId"), idValue(problem.problemId));
            body.insert(QStringLiteral("userId"), idValue(mSession.userId));
            request(QStringLiteral("POST"), QStringLiteral("/questionTk/getDetailById"), body, true,
                    [displayId, title, problem, onOk](const QJsonValue& data, const QString&) {
                AscStatement statement;
                if (data.isObject()) {
                    statement = Asc::statementFromDetail(data.toObject(), displayId, title);
                }
                statement.problemId = problem.problemId;
                if (statement.displayId.isEmpty())
                    statement.displayId = problem.displayId;
                if (statement.title.isEmpty())
                    statement.title = problem.title;
                onOk(statement);
            },
                    [firstError, onErr](const QString& secondError) {
                onErr(tr("Could not read the problem statement (%1; the fallback said: %2)")
                      .arg(firstError, secondError));
            });
        });
    }, onErr);
}

void AscClient::listClasses(const std::function<void(const QVector<AscClassInfo>& classes)>& onOk,
                            const ErrorHandler& onErr)
{
    request(QStringLiteral("GET"),
            QStringLiteral("/classroomAdmin/getClass?page=1&pageSize=50&search="),
            QJsonObject(), false,
            [onOk](const QJsonValue& data, const QString&) {
        onOk(Asc::classesFrom(data));
    }, onErr);
}

void AscClient::listTasks(AscProblemSource source, const QString& classId,
                          const std::function<void(const QVector<AscTaskInfo>& tasks)>& onOk,
                          const ErrorHandler& onErr)
{
    QSharedPointer<TaskPager> pager(new TaskPager());
    pager->source = source;
    pager->classId = classId;
    pager->page = 1;
    pager->total = -1;
    pager->onOk = onOk;
    pager->onErr = onErr;
    loadTasksPage(pager);
}

void AscClient::loadTasksPage(const QSharedPointer<TaskPager>& pager)
{
    const bool isWork = pager->source == AscProblemSource::Work;
    // Built with a query so the ids stay URL encoded.
    QUrlQuery query;
    query.addQueryItem(QStringLiteral("studentId"), mSession.userId);
    query.addQueryItem(QStringLiteral("classId"), pager->classId);
    query.addQueryItem(QStringLiteral("page"), QString::number(pager->page));
    query.addQueryItem(QStringLiteral("pageSize"), QString::number(ASC_TASK_PAGE_SIZE));
    QString path = QStringLiteral("/student/getAll%1?%2")
            .arg(isWork ? QStringLiteral("Work") : QStringLiteral("Test"), query.toString());

    request(QStringLiteral("GET"), path, QJsonObject(), false,
            [this, pager](const QJsonValue& data, const QString&) {
        pager->items += Asc::tasksFrom(data, pager->source);
        if (data.isObject())
            pager->total = Asc::numOf(data.toObject(), {QStringLiteral("total")}, pager->total);
        qlonglong wanted = pager->total < 0 ? pager->items.count() : pager->total;
        bool more = pager->items.count() < wanted && pager->page < ASC_TASK_PAGE_CAP;
        if (more && !pager->items.isEmpty()) {
            // A short page with no rows at all means the list is exhausted.
            pager->page++;
            loadTasksPage(pager);
            return;
        }
        pager->onOk(pager->items);
    }, pager->onErr);
}

void AscClient::listTaskProblems(AscProblemSource source, const QString& taskId,
                                 const std::function<void(const QVector<AscTaskProblem>& problems)>& onOk,
                                 const ErrorHandler& onErr)
{
    const bool isWork = source == AscProblemSource::Work;
    QUrlQuery query;
    query.addQueryItem(QStringLiteral("userId"), mSession.userId);
    query.addQueryItem(isWork ? QStringLiteral("workId") : QStringLiteral("testId"), taskId);
    QString path = QStringLiteral("/student/get%1List?%2")
            .arg(isWork ? QStringLiteral("Work") : QStringLiteral("Test"), query.toString());

    request(QStringLiteral("GET"), path, QJsonObject(), false,
            [source, onOk](const QJsonValue& data, const QString&) {
        onOk(Asc::taskProblemsFrom(data, source));
    }, onErr);
}

void AscClient::submitCode(const QString& code, const QString& language, const QString& problemId,
                           const std::function<void(const QString& submissionId)>& onOk,
                           const ErrorHandler& onErr)
{
    QJsonObject body;
    body.insert(QStringLiteral("code"), code);
    body.insert(QStringLiteral("language"), language);
    body.insert(QStringLiteral("problem_id"), idValue(problemId));

    request(QStringLiteral("POST"), QStringLiteral("/api/submission"), body, true,
            [onOk, onErr](const QJsonValue& data, const QString& message) {
        // The site puts the new submission id in "msg" and leaves "data" null.
        const QString submissionId = Asc::submissionIdFrom(data, message);
        if (submissionId.isEmpty()) {
            onErr(tr("The site accepted the submission but did not report its id."));
            return;
        }
        onOk(submissionId);
    }, onErr);
}

void AscClient::pollSubmission(const QString& submissionId, const QString& problemId,
                               const std::function<void(const AscSubmissionResult& result)>& onOk,
                               const ErrorHandler& onErr)
{
    // Note: never add a "usedTime" parameter here, the endpoint 500s when it is present.
    QUrlQuery query;
    query.addQueryItem(QStringLiteral("submission_id"), submissionId);
    query.addQueryItem(QStringLiteral("studentId"), mSession.userId);
    query.addQueryItem(QStringLiteral("tkId"), problemId);

    request(QStringLiteral("GET"), QStringLiteral("/api/submission?") + query.toString(),
            QJsonObject(), false,
            [onOk](const QJsonValue& data, const QString&) {
        onOk(Asc::submissionFrom(data));
    }, onErr);
}

void AscClient::registerTestAnswer(const QString& testId, const QString& questionId,
                                   const AscSubmissionResult& result, const ErrorHandler& onErr)
{
    QJsonObject body;
    body.insert(QStringLiteral("userid"), idValue(mSession.userId));
    body.insert(QStringLiteral("testid"), idValue(testId));
    body.insert(QStringLiteral("questionId"), idValue(questionId));
    body.insert(QStringLiteral("isDo"), 1);
    body.insert(QStringLiteral("subTime"), result.time);
    body.insert(QStringLiteral("isPass"), double(result.isPass));
    body.insert(QStringLiteral("records"), 1);
    body.insert(QStringLiteral("subStatic"), result.rawStatus);
    body.insert(QStringLiteral("score"), result.score);
    body.insert(QStringLiteral("tkSubId"), idValue(result.id));

    request(QStringLiteral("POST"), QStringLiteral("/student/submitTestAnswer"), body, true,
            [](const QJsonValue&, const QString&) {}, onErr);
}

void AscClient::registerWorkAnswer(const QString& workId, const QString& questionId,
                                   const QString& code, const AscSubmissionResult& result,
                                   const ErrorHandler& onErr)
{
    // Homework wants a different set of fields, and it carries the source.
    QJsonObject body;
    body.insert(QStringLiteral("id"), idValue(result.id));
    body.insert(QStringLiteral("isPass"), double(result.isPass));
    body.insert(QStringLiteral("userid"), idValue(mSession.userId));
    body.insert(QStringLiteral("subCode"), code);
    body.insert(QStringLiteral("codeLanguage"), QStringLiteral("C++"));
    body.insert(QStringLiteral("workid"), idValue(workId));
    body.insert(QStringLiteral("questionId"), idValue(questionId));
    body.insert(QStringLiteral("score"), result.score);
    body.insert(QStringLiteral("tkSubId"), idValue(result.id));

    request(QStringLiteral("POST"), QStringLiteral("/student/submitWorkAnswer"), body, true,
            [](const QJsonValue&, const QString&) {}, onErr);
}
