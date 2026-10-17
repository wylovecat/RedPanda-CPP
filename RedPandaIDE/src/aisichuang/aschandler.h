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
#ifndef ASCHANDLER_H
#define ASCHANDLER_H

#include "ascproblemimporter.h"
#include "ascproblemlinks.h"
#include "asctypes.h"

#include <QObject>
#include <QString>

#include <memory>

class AscClient;
class AscPanelWidget;
class AscSessionManager;
class QToolButton;
class QTimer;

/**
 * Wires the aisichuang integration into the main window: it owns the network
 * client, the login state, the importer and the dock page, and turns their
 * results into problem-set entries and status bar feedback.
 *
 * Submitting code back to the site is kept behind a setting
 * (enableOnlineSubmit) and a confirmation dialog, because unlike every other
 * action here it writes to the production teaching site.
 */
class AscHandler: public QObject
{
    Q_OBJECT
public:
    explicit AscHandler(QObject* parent = nullptr);
    ~AscHandler() override;

    /** The page to be hosted by the explorer dock. */
    AscPanelWidget* panel() const;

    /** The always-on indicator for the main window's status bar. */
    QToolButton* statusButton() const;

    /** Check a stored token against the site; call once the main window exists. */
    void restoreSession();

    bool isLoggedIn() const;
    bool isBusy() const;

    /** Re-read the options page (API root, spacing, submit language). */
    void applySettings();

    /** Language names the site accepts, once a session is alive. */
    QStringList languages() const;

    void openPanel();
    void showLogin();
    void logout();

    /** Compile the current file and run it against the selected problem's samples. */
    void testSamples();

    /** Re-download the statement and samples of the problem the editor is on. */
    void refreshCurrentProblem();

    /** Submit the current file to the site (needs the enableOnlineSubmit setting). */
    void submitCurrentFile();

private slots:
    void onLoggedIn(const AscSession& session);
    void onLoggedOut();
    void onLoginFailed(const QString& message);
    void onBusyChanged(bool busy);
    void onProblemImported(const AscImportedProblem& imported);
    void onStatusMessage(const QString& message);
    void onErrorMessage(const QString& message);
    void onStatusButtonClicked();

private:
    struct SubmitState
    {
        QString submissionId;
        QString problemId;
        QString questionId;
        QString code;
        QString language;
        QString displayId;
        AscProblemSource source;
        QString taskId;
        qint64 deadlineMs;
        bool waitingToStart;
    };

    void updateStatusText();
    void logLine(const QString& message);
    void startSubmission(const QString& code, const AscProblemLink& link);
    void pollSubmission();
    void finishSubmission(const AscSubmissionResult& result);
    void registerAnswer(const SubmitState& state, const AscSubmissionResult& result);

    AscClient* mClient;
    AscSessionManager* mSession;
    AscProblemImporter* mImporter;
    AscPanelWidget* mPanel;
    QToolButton* mStatusButton;

    QTimer* mPollTimer;
    std::shared_ptr<SubmitState> mSubmitting;
    QString mStatusText;
};

#endif // ASCHANDLER_H
