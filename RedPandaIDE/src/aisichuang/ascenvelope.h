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
#ifndef ASCENVELOPE_H
#define ASCENVELOPE_H

#include "asctypes.h"

#include <QJsonArray>
#include <QJsonObject>
#include <QJsonValue>
#include <QStringList>

/**
 * Pure helpers for the aisichuang.net endpoints. The site has no public API
 * documentation, so every rule encoded here comes from observing its own
 * front-end traffic; the quirks (all-lowercase "workid"/"testid", "isDo" for
 * tests versus "is_do" for homework, the submission id arriving in "msg")
 * must be preserved verbatim or the calls stop working.
 */
namespace Asc {

/** Success envelope is {code:200, msg, data}; anything else is an error. */
bool envelopeSuccess(const QJsonObject& body, qint64* code, QString* message);

/** A rejected/expired session: code 401, or a message asking the user to log in. */
bool isAuthFailure(qint64 code, const QString& message);

/** First key holding a usable string; numbers and booleans are stringified. */
QString strOf(const QJsonObject& obj, const QStringList& keys);

/** First key holding a usable number; numeric strings are accepted. */
qlonglong numOf(const QJsonObject& obj, const QStringList& keys, qlonglong defaultValue = 0);

bool boolOf(const QJsonObject& obj, const QStringList& keys, bool defaultValue = false);

/** The site returns either a bare array or a {records:[]}/{list:[]} object. */
QJsonArray arrayOf(const QJsonValue& data);

/**
 * OJ-style output normalization: CRLF/CR to LF, strip trailing blanks of every
 * line, drop the trailing empty lines. Used when writing sample files so the
 * IDE's own case validator (which compares line by line) sees a tidy expected.
 */
QString normalizeOutput(const QString& text);

/** Site verdict text to AC/WA/TLE/MLE/RE/CE/PE/"?". */
QString normalizeVerdict(const QString& raw);

/** Normalized verdict of a submission record, scanning the known field names. */
QString verdictOfRecord(const QJsonObject& record);

/**
 * Whether a polled record may be shown as a final result.
 *
 * Judging is asynchronous: right after submitting, the record already has
 * subIsPass != 7 but an empty subStatic and id 0, and reporting that as a
 * verdict would turn a real TLE into a false WA.
 */
bool isFinalResult(const AscSubmissionResult& result);

/** "138****90" so an account can be shown in a status bar. */
QString maskAccount(const QString& phone);

/** Problem id shape: a letter followed by word characters ending in digits (A1131). */
bool looksLikeProblemId(const QString& stem);

/** File name without its last extension. */
QString stemOf(const QString& fileName);

QString formatBytes(qlonglong bytes);

// Row mappers: site JSON to the structs in asctypes.h

QVector<AscProblemRef> problemRefsFrom(const QJsonValue& data);
QVector<AscClassInfo> classesFrom(const QJsonValue& data);
QVector<AscTaskInfo> tasksFrom(const QJsonValue& data, AscProblemSource source);

/**
 * Problems of a test/homework. The site only ships rows for every question
 * type; rows whose quesTypeId is not 5 are not programming problems and are
 * dropped here, matching the teaching-assistant behaviour.
 */
QVector<AscTaskProblem> taskProblemsFrom(const QJsonValue& data, AscProblemSource source);

AscSubmissionResult submissionFrom(const QJsonValue& data);

/**
 * The id of a fresh submission.
 *
 * The site's /api/submission answer puts the new id into "msg" and leaves
 * "data" null, so a caller that reads only "data" never gets an id to poll.
 */
QString submissionIdFrom(const QJsonValue& data, const QString& message);

/** Sample list; entries with an empty input and output are dropped. */
QVector<AscSample> pickSamples(const QJsonArray& samples);

}

#endif // ASCENVELOPE_H
