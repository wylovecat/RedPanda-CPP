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
#ifndef ASCSTATEMENT_H
#define ASCSTATEMENT_H

#include "asctypes.h"

#include <QJsonObject>

namespace Asc {

/** Statement from GET /api/getProblemOjDto (snake_case fields). */
AscStatement statementFromOjDto(const QJsonObject& dto, const QString& displayId, const QString& title);

/**
 * Statement from POST /questionTk/getDetailById (camelCase fields, plus the
 * site's own complete markdown in "all"). Used when the OjDto endpoint fails,
 * which it does for some problems.
 */
AscStatement statementFromDetail(const QJsonObject& dto, const QString& displayId, const QString& title);

/** Markdown used for the saved "<displayId>.md" file when the site gives none. */
QString statementToMarkdown(const AscStatement& statement);

}

#endif // ASCSTATEMENT_H
