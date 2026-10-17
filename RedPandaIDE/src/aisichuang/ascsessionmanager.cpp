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
#include "ascsessionmanager.h"

#include "ascclient.h"

#include "../settings.h"

AscSessionManager::AscSessionManager(AscClient* client, QObject* parent):
    QObject(parent),
    mClient(client),
    mBusy(false)
{
}

void AscSessionManager::restore()
{
    AscSession stored = pSettings->aisichuang().session();
    if (!stored.isValid())
        return;

    mClient->setBaseUrl(pSettings->aisichuang().baseUrl());
    mClient->setSession(stored);
    setBusy(true);

    // A stored token may already be dead; the /api/languages probe answers
    // that without side effects, and hands back the language names used by
    // the submission endpoint.
    mClient->probeSession([this](const QStringList& languages) {
        setBusy(false);
        mLanguages = languages;
        mSession = mClient->session();
        emit loggedIn(mSession);
    }, [this](const QString& message) {
        setBusy(false);
        mClient->setSession(AscSession());
        pSettings->aisichuang().saveSession(AscSession());
        emit loginFailed(message);
    });
}

void AscSessionManager::login(const QString& phone, const QString& password)
{
    if (busy())
        return;
    mClient->setBaseUrl(pSettings->aisichuang().baseUrl());
    setBusy(true);

    mClient->login(phone, password, [this, phone](const AscSession& session) {
        setBusy(false);
        mSession = session;
        pSettings->aisichuang().setLastPhone(phone);
        pSettings->aisichuang().saveSession(session);
        pSettings->aisichuang().save();
        // Cache the language list for the submit endpoint.
        mClient->probeSession([this](const QStringList& languages) {
            mLanguages = languages;
        }, [](const QString&) {});
        emit loggedIn(mSession);
    }, [this](const QString& message) {
        setBusy(false);
        emit loginFailed(message);
    });
}

void AscSessionManager::logout()
{
    mClient->cancelAll();
    mClient->setSession(AscSession());
    mSession = AscSession();
    mLanguages.clear();
    pSettings->aisichuang().saveSession(AscSession());
    pSettings->aisichuang().save();
    emit loggedOut();
}

bool AscSessionManager::isLoggedIn() const
{
    return mSession.isValid();
}

const AscSession& AscSessionManager::session() const
{
    return mSession;
}

const QStringList& AscSessionManager::languages() const
{
    return mLanguages;
}

bool AscSessionManager::busy() const
{
    return mBusy;
}

void AscSessionManager::setBusy(bool busy)
{
    if (mBusy == busy)
        return;
    mBusy = busy;
    emit busyChanged(mBusy);
}
