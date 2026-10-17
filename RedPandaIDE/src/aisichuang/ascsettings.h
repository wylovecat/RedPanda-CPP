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
#ifndef ASCSETTINGS_H
#define ASCSETTINGS_H

#include "../settings/basesettings.h"

#include "asctypes.h"

#define SETTING_AISICHUANG "Aisichuang"

#define DEFAULT_ASC_BASE_URL "https://www.aisichuang.net/tiku"

/**
 * Settings of the aisichuang.net integration.
 *
 * Only the session token bundle is persisted, never the password: the site's
 * login endpoint takes a plaintext phone + password and hands back a Sa-Token,
 * so keeping the password in the INI file would be a needless risk. When the
 * stored token expires the user is asked to log in again.
 */
class AscSettings: public BaseSettings
{
public:
    explicit AscSettings(SettingsPersistor* persistor);

    const QString& baseUrl() const;
    void setBaseUrl(const QString& newBaseUrl);

    bool enableOnlineSubmit() const;
    void setEnableOnlineSubmit(bool newEnableOnlineSubmit);

    const QString& submitLanguage() const;
    void setSubmitLanguage(const QString& newSubmitLanguage);

    int pollIntervalMs() const;
    void setPollIntervalMs(int newPollIntervalMs);

    int pollTimeoutMs() const;
    void setPollTimeoutMs(int newPollTimeoutMs);

    int throttleMs() const;
    void setThrottleMs(int newThrottleMs);

    const QString& taskRootDir() const;
    void setTaskRootDir(const QString& newTaskRootDir);

    bool saveStatementAsMarkdown() const;
    void setSaveStatementAsMarkdown(bool newSaveStatementAsMarkdown);

    const QString& codeTemplate() const;
    void setCodeTemplate(const QString& newCodeTemplate);

    const QString& lastPhone() const;
    void setLastPhone(const QString& newLastPhone);

    /** Persist a fresh session (or clear it when the given session is invalid). */
    void saveSession(const AscSession& session);

    /** The stored session; {@link AscSession::isValid} tells whether it may be usable. */
    AscSession session() const;

protected:
    void doSave() override;
    void doLoad() override;

private:
    QString mBaseUrl;
    bool mEnableOnlineSubmit;
    QString mSubmitLanguage;
    int mPollIntervalMs;
    int mPollTimeoutMs;
    int mThrottleMs;
    QString mTaskRootDir;
    bool mSaveStatementAsMarkdown;
    QString mCodeTemplate;
    QString mLastPhone;

    QString mSatoken;
    QString mCsrfToken;
    QString mSessionId;
    QString mUserId;
    QString mRole;
};

#endif // ASCSETTINGS_H
