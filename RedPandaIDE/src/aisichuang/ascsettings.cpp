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
#include "ascsettings.h"

#include <QDir>

AscSettings::AscSettings(SettingsPersistor* persistor):
    BaseSettings(persistor, SETTING_AISICHUANG),
    mBaseUrl(QString::fromLatin1(DEFAULT_ASC_BASE_URL)),
    mEnableOnlineSubmit(false),
    mSubmitLanguage(QStringLiteral("C++")),
    mPollIntervalMs(2000),
    mPollTimeoutMs(120000),
    mThrottleMs(150),
    mSaveStatementAsMarkdown(true),
    mCodeTemplate(QStringLiteral("#include <bits/stdc++.h>\n"
                                "using namespace std;\n"
                                "\n"
                                "int main() {\n"
                                "\n"
                                "    return 0;\n"
                                "}\n"))
{

}

const QString& AscSettings::baseUrl() const
{
    return mBaseUrl;
}

void AscSettings::setBaseUrl(const QString& newBaseUrl)
{
    mBaseUrl = newBaseUrl;
}

bool AscSettings::enableOnlineSubmit() const
{
    return mEnableOnlineSubmit;
}

void AscSettings::setEnableOnlineSubmit(bool newEnableOnlineSubmit)
{
    mEnableOnlineSubmit = newEnableOnlineSubmit;
}

const QString& AscSettings::submitLanguage() const
{
    return mSubmitLanguage;
}

void AscSettings::setSubmitLanguage(const QString& newSubmitLanguage)
{
    mSubmitLanguage = newSubmitLanguage;
}

int AscSettings::pollIntervalMs() const
{
    return mPollIntervalMs;
}

void AscSettings::setPollIntervalMs(int newPollIntervalMs)
{
    mPollIntervalMs = newPollIntervalMs;
}

int AscSettings::pollTimeoutMs() const
{
    return mPollTimeoutMs;
}

void AscSettings::setPollTimeoutMs(int newPollTimeoutMs)
{
    mPollTimeoutMs = newPollTimeoutMs;
}

int AscSettings::throttleMs() const
{
    return mThrottleMs;
}

void AscSettings::setThrottleMs(int newThrottleMs)
{
    mThrottleMs = newThrottleMs;
}

const QString& AscSettings::taskRootDir() const
{
    return mTaskRootDir;
}

void AscSettings::setTaskRootDir(const QString& newTaskRootDir)
{
    mTaskRootDir = newTaskRootDir;
}

bool AscSettings::saveStatementAsMarkdown() const
{
    return mSaveStatementAsMarkdown;
}

void AscSettings::setSaveStatementAsMarkdown(bool newSaveStatementAsMarkdown)
{
    mSaveStatementAsMarkdown = newSaveStatementAsMarkdown;
}

const QString& AscSettings::codeTemplate() const
{
    return mCodeTemplate;
}

void AscSettings::setCodeTemplate(const QString& newCodeTemplate)
{
    mCodeTemplate = newCodeTemplate;
}

const QString& AscSettings::lastPhone() const
{
    return mLastPhone;
}

void AscSettings::setLastPhone(const QString& newLastPhone)
{
    mLastPhone = newLastPhone;
}

void AscSettings::saveSession(const AscSession& session)
{
    mSatoken = session.satoken;
    mCsrfToken = session.csrftoken;
    mSessionId = session.sessionId;
    mUserId = session.userId;
    mRole = session.role;
    save();
}

AscSession AscSettings::session() const
{
    AscSession session;
    session.baseUrl = mBaseUrl;
    session.satoken = mSatoken;
    session.csrftoken = mCsrfToken;
    session.sessionId = mSessionId;
    session.userId = mUserId;
    session.role = mRole;
    session.phone = mLastPhone;
    return session;
}

void AscSettings::doSave()
{
    saveValue("base_url", mBaseUrl);
    saveValue("enable_online_submit", mEnableOnlineSubmit);
    saveValue("submit_language", mSubmitLanguage);
    saveValue("poll_interval_ms", mPollIntervalMs);
    saveValue("poll_timeout_ms", mPollTimeoutMs);
    saveValue("throttle_ms", mThrottleMs);
    saveValue("task_root_dir", mTaskRootDir);
    saveValue("save_statement_md", mSaveStatementAsMarkdown);
    saveValue("code_template", mCodeTemplate);
    saveValue("last_phone", mLastPhone);
    saveValue("satoken", mSatoken);
    saveValue("csrf_token", mCsrfToken);
    saveValue("session_id", mSessionId);
    saveValue("user_id", mUserId);
    saveValue("role", mRole);
}

void AscSettings::doLoad()
{
    mBaseUrl = stringValue("base_url", QString::fromLatin1(DEFAULT_ASC_BASE_URL));
    // Strip a trailing slash: every endpoint is appended to this value.
    while (mBaseUrl.endsWith(QLatin1Char('/')))
        mBaseUrl.chop(1);
    if (mBaseUrl.isEmpty())
        mBaseUrl = QString::fromLatin1(DEFAULT_ASC_BASE_URL);
    mEnableOnlineSubmit = boolValue("enable_online_submit", false);
    mSubmitLanguage = stringValue("submit_language", QStringLiteral("C++"));
    mPollIntervalMs = intValue("poll_interval_ms", 2000);
    mPollTimeoutMs = intValue("poll_timeout_ms", 120000);
    mThrottleMs = intValue("throttle_ms", 150);
    mTaskRootDir = stringValue("task_root_dir", QString());
    if (mTaskRootDir.isEmpty())
        mTaskRootDir = QDir::homePath() + QStringLiteral("/aisichuang");
    mSaveStatementAsMarkdown = boolValue("save_statement_md", true);
    mCodeTemplate = stringValue("code_template",
                                QStringLiteral("#include <bits/stdc++.h>\n"
                                               "using namespace std;\n"
                                               "\n"
                                               "int main() {\n"
                                               "\n"
                                               "    return 0;\n"
                                               "}\n"));
    mLastPhone = stringValue("last_phone", QString());
    mSatoken = stringValue("satoken", QString());
    mCsrfToken = stringValue("csrf_token", QString());
    mSessionId = stringValue("session_id", QString());
    mUserId = stringValue("user_id", QString());
    mRole = stringValue("role", QStringLiteral("student"));
}
