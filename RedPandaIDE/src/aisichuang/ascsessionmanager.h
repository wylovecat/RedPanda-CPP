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
#ifndef ASCSESSIONMANAGER_H
#define ASCSESSIONMANAGER_H

#include "asctypes.h"

#include <QObject>
#include <QStringList>

class AscClient;

/**
 * Keeps the aisichuang login state.
 *
 * Only the token bundle returned by the login endpoint is persisted. The
 * password itself is never written to disk: the site exchanges a plaintext
 * phone + password for a Sa-Token, so storing the password would buy nothing
 * but a leaked credential. When the stored token stops working the user is
 * asked to type it again.
 */
class AscSessionManager: public QObject
{
    Q_OBJECT
public:
    explicit AscSessionManager(AscClient* client, QObject* parent = nullptr);

    /** Read the stored token and check it against the site; safe to call at start-up. */
    void restore();

    void login(const QString& phone, const QString& password);
    void logout();

    bool isLoggedIn() const;
    const AscSession& session() const;

    /** Language names reported by the site; "C++" is the one this IDE submits. */
    const QStringList& languages() const;

    bool busy() const;

signals:
    void loggedIn(const AscSession& session);
    void loggedOut();
    void loginFailed(const QString& message);
    void busyChanged(bool busy);

private:
    void setBusy(bool busy);

    AscClient* mClient;
    AscSession mSession;
    QStringList mLanguages;
    bool mBusy;
};

#endif // ASCSESSIONMANAGER_H
