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
#ifndef TEST_AISICHUANG_H
#define TEST_AISICHUANG_H

#include <QObject>

#include <functional>

class AscClient;
struct AscSession;

/**
 * Contract tests for the aisichuang.net client.
 *
 * The offline cases run against recorded response shapes and pin the quirks
 * that are easy to "tidy up" into brokenness (all-lowercase workid/testid,
 * isDo versus is_do, the submission id arriving in msg, subIsPass 7 meaning
 * "still judging"). The live cases talk to the real site and are skipped
 * unless ASC_LIVE is set, because the site is an unofficial third-party
 * service whose owners may change it at any time. Live cases only read:
 * nothing here submits code or writes to the site.
 */
class TestAisichuang: public QObject
{
    Q_OBJECT
public:
    using DoneFn = std::function<void()>;
    using FailFn = std::function<void(const QString&)>;

private slots:
    void initTestCase();

    void envelope_success_and_failure();
    void auth_failure_detection();
    void tolerant_field_readers();
    void normalize_output_is_oj_style();
    void verdict_normalization();
    void submission_finality_rule();
    void submission_id_lives_in_msg();
    void task_rows_use_lowercase_ids();
    void task_problems_filter_and_done_flag();
    void class_rows_either_array_or_records();
    void statement_from_oj_dto();
    void statement_from_detail_and_markdown();
    void samples_shape_variants();
    void problem_id_shape_and_masking();
    void problem_files_and_cases_are_written();

    void live_login_and_statement();
    void live_class_and_task_lists();

private:
    /** Run one asynchronous client call to completion; returns the error text. */
    QString runStep(AscClient& client, const std::function<void(DoneFn, FailFn)>& call);

    /** Log in with the credentials from the environment; false when it fails. */
    bool liveLogin(AscClient& client, AscSession& session, QString* error);

    bool mLiveEnabled;
    QString mPhone;
    QString mPassword;
};

#endif // TEST_AISICHUANG_H
