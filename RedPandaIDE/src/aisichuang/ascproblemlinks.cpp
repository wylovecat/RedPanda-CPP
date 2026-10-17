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
#include "ascproblemlinks.h"

#include "ascenvelope.h"

#include "../settings.h"
#include "../systemconsts.h"
#include "../utils.h"

#include <QDir>
#include <QFile>
#include <QFileInfo>
#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonObject>

static QString sourceText(AscProblemSource source)
{
    switch (source) {
    case AscProblemSource::Test:
        return QStringLiteral("test");
    case AscProblemSource::Work:
        return QStringLiteral("work");
    case AscProblemSource::Bank:
        break;
    }
    return QStringLiteral("bank");
}

static AscProblemSource sourceOf(const QString& text)
{
    if (text == QLatin1String("test"))
        return AscProblemSource::Test;
    if (text == QLatin1String("work"))
        return AscProblemSource::Work;
    return AscProblemSource::Bank;
}

static QString normalizedKey(const QString& path)
{
    return QDir::cleanPath(QFileInfo(path).absoluteFilePath());
}

AscProblemLinks& AscProblemLinks::instance()
{
    static AscProblemLinks links;
    return links;
}

QString AscProblemLinks::configFile()
{
    return includeTrailingPathDelimiter(pSettings->dirs().config())
            + DEV_AISICHUANG_FILE;
}

void AscProblemLinks::load()
{
    mLinks.clear();
    mLoaded = true;
    if (!fileExists(configFile()))
        return;

    QByteArray content = readFileToByteArray(configFile());
    QJsonParseError error;
    QJsonDocument document = QJsonDocument::fromJson(content, &error);
    if (error.error != QJsonParseError::NoError || !document.isObject())
        return;

    const QJsonArray rows = document.object().value(QStringLiteral("links")).toArray();
    for (const QJsonValue& value : rows) {
        if (!value.isObject())
            continue;
        QJsonObject row = value.toObject();
        AscProblemLink link;
        link.displayId = Asc::strOf(row, {QStringLiteral("displayId")});
        if (link.displayId.isEmpty())
            continue;
        link.problemId = Asc::strOf(row, {QStringLiteral("problemId")});
        link.title = Asc::strOf(row, {QStringLiteral("title")});
        link.source = sourceOf(Asc::strOf(row, {QStringLiteral("source")}));
        link.taskId = Asc::strOf(row, {QStringLiteral("taskId")});
        link.taskName = Asc::strOf(row, {QStringLiteral("taskName")});
        link.classId = Asc::strOf(row, {QStringLiteral("classId")});
        link.className = Asc::strOf(row, {QStringLiteral("className")});
        mLinks.insert(normalizedKey(Asc::strOf(row, {QStringLiteral("path")})), link);
    }
}

bool AscProblemLinks::save() const
{
    QJsonArray rows;
    for (auto it = mLinks.constBegin(); it != mLinks.constEnd(); ++it) {
        const AscProblemLink& link = it.value();
        QJsonObject row;
        row.insert(QStringLiteral("path"), it.key());
        row.insert(QStringLiteral("displayId"), link.displayId);
        row.insert(QStringLiteral("problemId"), link.problemId);
        row.insert(QStringLiteral("title"), link.title);
        row.insert(QStringLiteral("source"), sourceText(link.source));
        row.insert(QStringLiteral("taskId"), link.taskId);
        row.insert(QStringLiteral("taskName"), link.taskName);
        row.insert(QStringLiteral("classId"), link.classId);
        row.insert(QStringLiteral("className"), link.className);
        rows.append(row);
    }
    QJsonObject root;
    root.insert(QStringLiteral("links"), rows);

    QFile file(configFile());
    if (!file.open(QIODevice::WriteOnly | QIODevice::Truncate))
        return false;
    file.write(QJsonDocument(root).toJson(QJsonDocument::Indented));
    file.close();
    return true;
}

void AscProblemLinks::put(const QString& sourcePath, const AscProblemLink& link)
{
    if (!mLoaded)
        load();
    mLinks.insert(normalizedKey(sourcePath), link);
    save();
}

void AscProblemLinks::remove(const QString& sourcePath)
{
    if (!mLoaded)
        load();
    if (mLinks.remove(normalizedKey(sourcePath)) > 0)
        save();
}

AscProblemLink AscProblemLinks::linkFor(const QString& sourcePath) const
{
    AscProblemLink link;
    if (sourcePath.isEmpty())
        return link;

    auto found = mLinks.constFind(normalizedKey(sourcePath));
    if (found != mLinks.constEnd())
        return found.value();

    // "A1131.cpp" is a bank problem even when it was never imported.
    QString stem = Asc::stemOf(extractFileName(sourcePath));
    if (Asc::looksLikeProblemId(stem)) {
        link.displayId = stem;
        link.source = AscProblemSource::Bank;
    }
    return link;
}
