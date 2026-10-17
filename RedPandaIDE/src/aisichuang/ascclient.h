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
#ifndef ASCCLIENT_H
#define ASCCLIENT_H

#include "asctypes.h"

#include <QJsonArray>
#include <QJsonObject>
#include <QJsonValue>
#include <QQueue>
#include <QSharedPointer>
#include <QStringList>
#include <QUrl>

#include <QObject>
#include <functional>

class QNetworkAccessManager;
class QNetworkReply;
class QTimer;

/**
 * Client for the aisichuang.net teaching site.
 *
 * The site publishes no API documentation; every endpoint, header and field
 * name here mirrors what its own front-end does. It is a third party service
 * that may change without notice, so all parsing goes through the tolerant
 * helpers in ascenvelope and every failure ends up in the error callback
 * rather than in an exception or a crash.
 *
 * Everything runs on the GUI thread with QNetworkAccessManager's signals:
 * requests are put on an internal queue and pumped one at a time with a
 * throttle in between, because the site dislikes parallel calls from one
 * session. Callbacks are std::function values moved into the queue, so no
 * dangling receiver can be hit; {@link cancelAll} drops the queue and ignores
 * any reply that is still arriving.
 */
class AscClient: public QObject
{
    Q_OBJECT
public:
    using ErrorHandler = std::function<void(const QString& message)>;

    explicit AscClient(QObject* parent = nullptr);
    ~AscClient() override;

    void setBaseUrl(const QString& baseUrl);
    QString baseUrl() const;

    void setSession(const AscSession& session);
    const AscSession& session() const;
    bool isLoggedIn() const;

    void setThrottleMs(int ms);

    /** Drop queued work, abort the in-flight reply and ignore late callbacks. */
    void cancelAll();

    /**
     * Login is a two step dance: fetch a CSRF token from /api/profile (a
     * failure there is tolerated), then post the phone and password. The site
     * compares the password in the clear, so nothing is hashed here.
     */
    void login(const QString& phone, const QString& password,
               const std::function<void(const AscSession& session)>& onOk,
               const ErrorHandler& onErr);

    /** Probe the stored session; on success the site's language list is returned. */
    void probeSession(const std::function<void(const QStringList& languages)>& onOk,
                      const ErrorHandler& onErr);

    void searchProblems(const QString& keyword, int page,
                        const std::function<void(const QVector<AscProblemRef>& problems, qlonglong total)>& onOk,
                        const ErrorHandler& onErr);

    /** Resolve a display id such as A1131 to its bank row; an unmatched search is an error. */
    void findProblem(const QString& displayId,
                     const std::function<void(const AscProblemRef& problem)>& onOk,
                     const ErrorHandler& onErr);

    /**
     * Statement plus sample cases of one problem, addressed by display id.
     * Tries the snake_case endpoint and falls back to the camelCase one.
     */
    void fetchStatement(const QString& displayId, const QString& title,
                        const std::function<void(const AscStatement& statement)>& onOk,
                        const ErrorHandler& onErr);

    /** Teacher/admin only; students get an error which callers should tolerate. */
    void listClasses(const std::function<void(const QVector<AscClassInfo>& classes)>& onOk,
                     const ErrorHandler& onErr);

    /** All tests (or homework) of one class; pages through the site's list. */
    void listTasks(AscProblemSource source, const QString& classId,
                   const std::function<void(const QVector<AscTaskInfo>& tasks)>& onOk,
                   const ErrorHandler& onErr);

    void listTaskProblems(AscProblemSource source, const QString& taskId,
                          const std::function<void(const QVector<AscTaskProblem>& problems)>& onOk,
                          const ErrorHandler& onErr);

    /** Submit code to the bank; the id of the new submission comes back in "msg". */
    void submitCode(const QString& code, const QString& language, const QString& problemId,
                    const std::function<void(const QString& submissionId)>& onOk,
                    const ErrorHandler& onErr);

    /** One poll of a submission; callers retry until {@link Asc::isFinalResult}. */
    void pollSubmission(const QString& submissionId, const QString& problemId,
                        const std::function<void(const AscSubmissionResult& result)>& onOk,
                        const ErrorHandler& onErr);

    /** Second half of a task submission: record the answer against the test. */
    void registerTestAnswer(const QString& testId, const QString& questionId,
                            const AscSubmissionResult& result, const ErrorHandler& onErr);

    /** Homework wants a different field set, including the source code itself. */
    void registerWorkAnswer(const QString& workId, const QString& questionId, const QString& code,
                            const AscSubmissionResult& result, const ErrorHandler& onErr);

signals:
    /** The stored token was rejected: the user has to log in again. */
    void authExpired();

private:
    using DataHandler = std::function<void(const QJsonValue& data, const QString& message)>;
    using Job = std::function<void()>;

    struct TaskPager
    {
        AscProblemSource source;
        QString classId;
        int page;
        qlonglong total;
        QVector<AscTaskInfo> items;
        std::function<void(const QVector<AscTaskInfo>& tasks)> onOk;
        ErrorHandler onErr;
    };

    void enqueue(const Job& job);
    void pump();
    void releasePump();

    void request(const QString& method, const QString& path, const QJsonObject& body,
                 bool withCsrf, const DataHandler& onOk, const ErrorHandler& onErr,
                 const QString& csrfOverride = QString());
    void startRequest(const QString& method, const QString& path, const QJsonObject& body,
                      bool withCsrf, const QString& csrfOverride,
                      const DataHandler& onOk, const ErrorHandler& onErr);
    QUrl buildUrl(const QString& path) const;

    void loadTasksPage(const QSharedPointer<TaskPager>& pager);

    static QString describeHttpFailure(QNetworkReply* reply);

private:
    QNetworkAccessManager* mNetwork;
    QTimer* mPumpTimer;
    QQueue<Job> mQueue;
    QNetworkReply* mCurrentReply;
    bool mExecuting;
    int mGeneration;
    int mThrottleMs;
    QString mBaseUrl;
    AscSession mSession;
};

#endif // ASCCLIENT_H
