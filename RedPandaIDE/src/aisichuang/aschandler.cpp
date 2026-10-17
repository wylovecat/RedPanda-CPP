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
#include "aschandler.h"

#include "ascclient.h"
#include "ascenvelope.h"
#include "ascsessionmanager.h"
#include "ascpanelwidget.h"
#include "ascproblemlinks.h"

#include "../editor.h"
#include "../editormanager.h"
#include "../mainwindow.h"
#include "../settings.h"

#include <QDateTime>
#include <QMessageBox>
#include <QTimer>
#include <QToolButton>

/** The site finishes judging asynchronously; wait a beat before the first poll. */
static const int ASC_FIRST_POLL_DELAY_MS = 2000;

AscHandler::AscHandler(QObject* parent):
    QObject(parent),
    mClient(new AscClient(this)),
    mSession(new AscSessionManager(mClient, this)),
    mImporter(new AscProblemImporter(mClient, this)),
    mPanel(nullptr),
    mStatusButton(new QToolButton()),
    mPollTimer(new QTimer(this))
{
    mPanel = new AscPanelWidget(mClient, mSession, mImporter, nullptr);

    mClient->setBaseUrl(pSettings->aisichuang().baseUrl());
    mClient->setThrottleMs(pSettings->aisichuang().throttleMs());

    mPollTimer->setSingleShot(true);
    connect(mPollTimer, &QTimer::timeout, this, &AscHandler::pollSubmission);

    connect(mSession, &AscSessionManager::loggedIn, this, &AscHandler::onLoggedIn);
    connect(mSession, &AscSessionManager::loggedOut, this, &AscHandler::onLoggedOut);
    connect(mSession, &AscSessionManager::loginFailed, this, &AscHandler::onLoginFailed);
    connect(mSession, &AscSessionManager::busyChanged, this, &AscHandler::onBusyChanged);

    connect(mPanel, &AscPanelWidget::problemImported, this, &AscHandler::onProblemImported);
    connect(mPanel, &AscPanelWidget::statusMessage, this, &AscHandler::onStatusMessage);
    connect(mPanel, &AscPanelWidget::errorMessage, this, &AscHandler::onErrorMessage);
    connect(mPanel, &AscPanelWidget::runSamplesRequested, this, [this]() {
        testSamples();
    });

    // A rejected token means the user has to type the password again. The
    // logout is deferred so the callback that detected it can finish first.
    connect(mClient, &AscClient::authExpired, this, [this]() {
        QTimer::singleShot(0, this, [this]() {
            mSession->logout();
        });
    });

    mStatusButton->setAutoRaise(true);
    mStatusButton->setToolTip(tr("Aisichuang: click to log in or open the task panel"));
    connect(mStatusButton, &QToolButton::clicked, this, &AscHandler::onStatusButtonClicked);
    updateStatusText();
}

AscHandler::~AscHandler()
{
    mClient->cancelAll();
    delete mPanel;
}

AscPanelWidget* AscHandler::panel() const
{
    return mPanel;
}

QToolButton* AscHandler::statusButton() const
{
    return mStatusButton;
}

void AscHandler::restoreSession()
{
    mSession->restore();
}

bool AscHandler::isLoggedIn() const
{
    return mSession->isLoggedIn();
}

bool AscHandler::isBusy() const
{
    return mSession->busy();
}

void AscHandler::applySettings()
{
    mClient->setBaseUrl(pSettings->aisichuang().baseUrl());
    mClient->setThrottleMs(pSettings->aisichuang().throttleMs());
}

QStringList AscHandler::languages() const
{
    return mSession->languages();
}

void AscHandler::openPanel()
{
    pMainWindow->showAisichuangPanel();
    mPanel->updateSessionState();
}

void AscHandler::showLogin()
{
    pMainWindow->showAisichuangPanel();
    mPanel->beginLogin();
}

void AscHandler::logout()
{
    mSession->logout();
}

void AscHandler::testSamples()
{
    pMainWindow->runSelectedProblemCases();
}

void AscHandler::refreshCurrentProblem()
{
    Editor* editor = pMainWindow->editorManager()->getEditor();
    if (!editor)
        return;
    AscProblemLink link = AscProblemLinks::instance().linkFor(editor->filename());
    if (!link.isValid()) {
        onErrorMessage(tr("Aisichuang: this file is not linked to an online problem. "
                          "Create it from the panel first, or name it after the problem id."));
        return;
    }
    if (!mSession->isLoggedIn()) {
        showLogin();
        return;
    }
    AscImportContext context;
    context.source = link.source;
    context.taskId = link.taskId;
    context.taskName = link.taskName;
    context.classId = link.classId;
    context.className = link.className;
    mImporter->importProblem(link.displayId, link.title, context,
                             AscPanelWidget::problemFolder(link.source, link.taskName),
                             pSettings->aisichuang().codeTemplate(),
                             pSettings->aisichuang().saveStatementAsMarkdown());
}

void AscHandler::submitCurrentFile()
{
    if (!pSettings->aisichuang().enableOnlineSubmit()) {
        onErrorMessage(tr("Aisichuang: submitting code to the site is turned off. "
                          "Enable it under Options - Aisichuang if you want it."));
        return;
    }
    if (!mSession->isLoggedIn()) {
        showLogin();
        return;
    }
    if (mSubmitting) {
        onStatusMessage(tr("Aisichuang: a submission is already being judged."));
        return;
    }

    Editor* editor = pMainWindow->editorManager()->getEditor();
    if (!editor)
        return;
    const QString filename = editor->filename();
    AscProblemLink link = AscProblemLinks::instance().linkFor(filename);
    if (!link.isValid()) {
        onErrorMessage(tr("Aisichuang: %1 does not look like an aisichuang problem "
                          "(the file name should be the problem id).").arg(filename));
        return;
    }

    const QString code = editor->text();
    if (code.trimmed().isEmpty()) {
        onErrorMessage(tr("Aisichuang: the file is empty."));
        return;
    }

    // This is the one action that changes something outside the machine.
    QString where = QStringLiteral("%1").arg(link.displayId);
    if (link.isTask())
        where += QStringLiteral(" / %1").arg(link.taskName);
    if (QMessageBox::question(pMainWindow, tr("Submit to Aisichuang"),
            tr("Submit this code as the answer of %1?\n\n"
               "This writes to the teaching site and the teacher will see it.")
            .arg(where),
            QMessageBox::Yes | QMessageBox::No, QMessageBox::No) != QMessageBox::Yes) {
        return;
    }

    if (!link.problemId.isEmpty()) {
        startSubmission(code, link);
        return;
    }
    // Hand-written files may not know the bank id yet; the site wants it.
    onStatusMessage(tr("Aisichuang: looking up %1 ...").arg(link.displayId));
    AscProblemLink resolved = link;
    const QString linkKey = filename;
    mClient->findProblem(link.displayId,
            [this, code, resolved, linkKey](const AscProblemRef& problem) {
        AscProblemLink copy = resolved;
        copy.problemId = problem.problemId;
        if (copy.title.isEmpty())
            copy.title = problem.title;
        // Remember it so the next submission of this file needs no lookup.
        AscProblemLinks::instance().put(linkKey, copy);
        startSubmission(code, copy);
    }, [this, link](const QString& message) {
        onErrorMessage(tr("Aisichuang: %1 was not found in the question bank (%2).")
                       .arg(link.displayId, message));
    });
}

void AscHandler::startSubmission(const QString& code, const AscProblemLink& link)
{
    const QString language = pSettings->aisichuang().submitLanguage();
    mSubmitting = std::make_shared<SubmitState>();
    mSubmitting->code = code;
    mSubmitting->language = language;
    mSubmitting->problemId = link.problemId;
    mSubmitting->questionId = link.problemId;
    mSubmitting->displayId = link.displayId;
    mSubmitting->source = link.source;
    mSubmitting->taskId = link.taskId;
    mSubmitting->deadlineMs = QDateTime::currentMSecsSinceEpoch()
            + pSettings->aisichuang().pollTimeoutMs();
    mSubmitting->waitingToStart = true;

    onStatusMessage(tr("Aisichuang: submitting %1 ...").arg(link.displayId));
    std::shared_ptr<SubmitState> state = mSubmitting;
    mClient->submitCode(code, language, link.problemId,
            [this, state](const QString& submissionId) {
        if (mSubmitting != state)
            return;
        state->submissionId = submissionId;
        // The judge needs a moment before the record is worth reading.
        mPollTimer->start(ASC_FIRST_POLL_DELAY_MS);
        onStatusMessage(tr("Aisichuang: submitted, waiting for the judge ..."));
    }, [this, state](const QString& message) {
        if (mSubmitting != state)
            return;
        mSubmitting.reset();
        onErrorMessage(tr("Aisichuang: submission failed: %1").arg(message));
    });
}

void AscHandler::pollSubmission()
{
    std::shared_ptr<SubmitState> state = mSubmitting;
    if (!state)
        return;
    state->waitingToStart = false;

    mClient->pollSubmission(state->submissionId, state->problemId,
            [this, state](const AscSubmissionResult& result) {
        if (mSubmitting != state)
            return;
        if (Asc::isFinalResult(result)) {
            finishSubmission(result);
            return;
        }
        if (QDateTime::currentMSecsSinceEpoch() > state->deadlineMs) {
            mSubmitting.reset();
            onErrorMessage(tr("Aisichuang: the site is still judging %1; try the statement "
                              "panel or the website later.").arg(state->displayId));
            return;
        }
        mPollTimer->start(pSettings->aisichuang().pollIntervalMs());
    }, [this, state](const QString& message) {
        if (mSubmitting != state)
            return;
        mSubmitting.reset();
        onErrorMessage(tr("Aisichuang: could not read the judging result: %1").arg(message));
    });
}

void AscHandler::finishSubmission(const AscSubmissionResult& result)
{
    std::shared_ptr<SubmitState> state = mSubmitting;
    if (!state)
        return;
    mSubmitting.reset();

    QString summary = tr("Aisichuang: %1 -> %2 (%3 ms, %4)")
            .arg(state->displayId, result.verdict)
            .arg(result.timeMs)
            .arg(Asc::formatBytes(result.memoryBytes));
    if (!result.feedback.isEmpty())
        summary += QStringLiteral(" - %1").arg(result.feedback.left(200));
    onStatusMessage(summary);
    logLine(summary);

    if (!state->taskId.isEmpty() && !state->problemId.isEmpty())
        registerAnswer(*state, result);
}

void AscHandler::registerAnswer(const SubmitState& state, const AscSubmissionResult& result)
{
    // Classroom tests and homework take different field sets: the test call
    // records the judging outcome, the homework call ships the source itself.
    auto failed = [this](const QString& message) {
        onErrorMessage(tr("Aisichuang: the code was judged, but recording the answer failed: %1")
                       .arg(message));
    };

    onStatusMessage(tr("Aisichuang: recording the answer for the task ..."));
    if (state.source == AscProblemSource::Work) {
        mClient->registerWorkAnswer(state.taskId, state.problemId, state.code, result, failed);
    } else {
        mClient->registerTestAnswer(state.taskId, state.problemId, result, failed);
    }
}

void AscHandler::onLoggedIn(const AscSession& session)
{
    mPanel->updateSessionState();
    updateStatusText();
    onStatusMessage(tr("Aisichuang: logged in as %1 (%2)")
                    .arg(Asc::maskAccount(session.phone), session.role));
    pMainWindow->updateAisichuangActions();
}

void AscHandler::onLoggedOut()
{
    mClient->cancelAll();
    mSubmitting.reset();
    mPollTimer->stop();
    mPanel->updateSessionState();
    updateStatusText();
    pMainWindow->updateAisichuangActions();
}

void AscHandler::onLoginFailed(const QString& message)
{
    mPanel->updateSessionState();
    updateStatusText();
    onErrorMessage(tr("Aisichuang: login failed: %1").arg(message));
}

void AscHandler::onBusyChanged(bool busy)
{
    if (busy)
        mStatusButton->setText(tr("Aisichuang: ..."));
    else
        updateStatusText();
    pMainWindow->updateAisichuangActions();
}

void AscHandler::onProblemImported(const AscImportedProblem& imported)
{
    if (!imported.problem)
        return;
    pMainWindow->addProblemToProblemSet(imported.problem);
    pMainWindow->selectProblemInProblemSet(imported.problem);

    AscProblemLink link;
    link.displayId = imported.statement.displayId;
    link.problemId = imported.statement.problemId;
    link.title = imported.statement.title;
    link.source = imported.context.source;
    link.taskId = imported.context.taskId;
    link.taskName = imported.context.taskName;
    link.classId = imported.context.classId;
    link.className = imported.context.className;
    if (!link.displayId.isEmpty()) {
        // Remember the identity so the file can be submitted later.
        AscProblemLinks::instance().put(imported.sourcePath, link);
    }

    onStatusMessage(tr("Aisichuang: %1 imported with %2 sample(s) into %3")
                    .arg(link.displayId)
                    .arg(imported.statement.samples.count())
                    .arg(imported.folder));
    updateStatusText();
}

void AscHandler::onStatusMessage(const QString& message)
{
    mStatusText = message;
    updateStatusText();
    pMainWindow->updateStatusbarMessage(message);
}

void AscHandler::onErrorMessage(const QString& message)
{
    mStatusText = message;
    updateStatusText();
    logLine(message);
    QMessageBox::warning(pMainWindow, tr("Aisichuang"), message);
}

void AscHandler::logLine(const QString& message)
{
    pMainWindow->logToolsOutput(message);
}

void AscHandler::onStatusButtonClicked()
{
    openPanel();
    if (!mSession->isLoggedIn())
        mPanel->beginLogin();
}

void AscHandler::updateStatusText()
{
    QString text;
    if (mSession->busy())
        text = tr("Aisichuang: ...");
    else if (mSession->isLoggedIn())
        text = tr("Aisichuang: %1").arg(Asc::maskAccount(mSession->session().phone));
    else
        text = tr("Aisichuang: not logged in");
    mStatusButton->setText(text);
    QString tooltip = mStatusText.isEmpty()
            ? tr("Click to log in or open the aisichuang task panel")
            : mStatusText;
    mStatusButton->setToolTip(tooltip);
}
