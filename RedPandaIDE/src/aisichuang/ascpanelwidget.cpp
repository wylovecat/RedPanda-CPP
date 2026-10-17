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
#include "ascpanelwidget.h"

#include "ascclient.h"
#include "ascenvelope.h"
#include "ascstatement.h"
#include "ascsessionmanager.h"

#include "../settings.h"

#include <QComboBox>
#include <QFormLayout>
#include <QHBoxLayout>
#include <QHeaderView>
#include <QLabel>
#include <QLineEdit>
#include <QListWidget>
#include <QPushButton>
#include <QTableWidget>
#include <QTabWidget>
#include <QTextBrowser>
#include <QDir>
#include <QVBoxLayout>

AscPanelWidget::AscPanelWidget(AscClient* client, AscSessionManager* session,
                               AscProblemImporter* importer, QWidget* parent):
    QWidget(parent),
    mClient(client),
    mSession(session),
    mImporter(importer),
    mLoadedSource(AscProblemSource::Bank)
{
    QVBoxLayout* layout = new QVBoxLayout(this);
    layout->setContentsMargins(4, 4, 4, 4);

    mStatusLabel = new QLabel(this);
    mStatusLabel->setWordWrap(true);
    layout->addWidget(mStatusLabel);

    mTabs = new QTabWidget(this);
    mTabs->addTab(buildAccountPage(), tr("Account"));
    mTabs->addTab(buildTaskPage(), tr("Tasks"));
    mTabs->addTab(buildBankPage(), tr("Bank"));
    mTabs->addTab(buildStatementPage(), tr("Statement"));
    layout->addWidget(mTabs, 1);

    connect(mImporter, &AscProblemImporter::importStarted, this,
            [this](const QString& displayId) {
        setBusyIndicator(tr("Fetching %1 ...").arg(displayId));
    });
    connect(mImporter, &AscProblemImporter::importFailed, this,
            [this](const QString& displayId, const QString& message) {
        clearBusyIndicator();
        emit errorMessage(tr("Problem %1: %2").arg(displayId, message));
    });
    connect(mImporter, &AscProblemImporter::importFinished, this,
            [this](const AscImportedProblem& imported) {
        clearBusyIndicator();
        showStatement(imported.statement);
        emit problemImported(imported);
    });

    updateSessionState();
}

QWidget* AscPanelWidget::buildAccountPage()
{
    QWidget* page = new QWidget(this);
    QVBoxLayout* box = new QVBoxLayout(page);

    QFormLayout* form = new QFormLayout;
    mPhoneEdit = new QLineEdit(page);
    mPhoneEdit->setPlaceholderText(tr("Phone number"));
    mPasswordEdit = new QLineEdit(page);
    mPasswordEdit->setEchoMode(QLineEdit::Password);
    mPasswordEdit->setPlaceholderText(tr("Password"));
    form->addRow(tr("Account"), mPhoneEdit);
    form->addRow(tr("Password"), mPasswordEdit);
    box->addLayout(form);

    QHBoxLayout* buttons = new QHBoxLayout;
    mLoginButton = new QPushButton(tr("Log In"), page);
    mLogoutButton = new QPushButton(tr("Log Out"), page);
    buttons->addWidget(mLoginButton);
    buttons->addWidget(mLogoutButton);
    buttons->addStretch();
    box->addLayout(buttons);

    QLabel* hint = new QLabel(tr("The password is used once to ask the site for a session "
                                 "token and is never stored. Only the token is kept, so an "
                                 "expired session has to be logged in again."), page);
    hint->setWordWrap(true);
    box->addWidget(hint);
    box->addStretch();

    connect(mLoginButton, &QPushButton::clicked, this, &AscPanelWidget::onLoginClicked);
    connect(mLogoutButton, &QPushButton::clicked, this, &AscPanelWidget::onLogoutClicked);
    connect(mPasswordEdit, &QLineEdit::returnPressed, this, &AscPanelWidget::onLoginClicked);

    return page;
}

QWidget* AscPanelWidget::buildTaskPage()
{
    QWidget* page = new QWidget(this);
    QVBoxLayout* box = new QVBoxLayout(page);

    // Teachers browse by class; students only know their own test/work ids,
    // because the class list endpoint is not open to them.
    QHBoxLayout* classRow = new QHBoxLayout;
    mClassCombo = new QComboBox(page);
    mRefreshClassButton = new QPushButton(tr("Reload"), page);
    mRefreshClassButton->setToolTip(tr("Reload the class list (teacher accounts only)"));
    classRow->addWidget(new QLabel(tr("Class"), page));
    classRow->addWidget(mClassCombo, 1);
    classRow->addWidget(mRefreshClassButton);
    box->addLayout(classRow);

    QHBoxLayout* listRow = new QHBoxLayout;
    mTaskKindCombo = new QComboBox(page);
    mTaskKindCombo->addItem(tr("Classroom tests"));
    mTaskKindCombo->addItem(tr("Homework"));
    mLoadTaskButton = new QPushButton(tr("List"), page);
    mLoadTaskButton->setToolTip(tr("Load the tests or homework of the selected class"));
    listRow->addWidget(mTaskKindCombo, 1);
    listRow->addWidget(mLoadTaskButton);
    box->addLayout(listRow);

    QHBoxLayout* pickRow = new QHBoxLayout;
    mTaskCombo = new QComboBox(page);
    mOpenTaskButton = new QPushButton(tr("Open"), page);
    mOpenTaskButton->setToolTip(tr("Load the problems of the selected test or homework"));
    pickRow->addWidget(mTaskCombo, 1);
    pickRow->addWidget(mOpenTaskButton);
    box->addLayout(pickRow);

    QHBoxLayout* idRow = new QHBoxLayout;
    mTaskIdEdit = new QLineEdit(page);
    mTaskIdEdit->setPlaceholderText(tr("test id or work id"));
    mLoadTaskIdButton = new QPushButton(tr("Go"), page);
    mLoadTaskIdButton->setToolTip(tr("Type the numeric id of a test or homework directly"));
    idRow->addWidget(mTaskIdEdit, 1);
    idRow->addWidget(mLoadTaskIdButton);
    box->addLayout(idRow);

    mProblemTable = new QTableWidget(0, 3, page);
    mProblemTable->setHorizontalHeaderLabels({tr("No."), tr("Title"), tr("Done")});
    mProblemTable->horizontalHeader()->setSectionResizeMode(1, QHeaderView::Stretch);
    mProblemTable->horizontalHeader()->resizeSection(0, 70);
    mProblemTable->horizontalHeader()->resizeSection(2, 46);
    mProblemTable->verticalHeader()->setVisible(false);
    mProblemTable->setSelectionBehavior(QAbstractItemView::SelectRows);
    mProblemTable->setEditTriggers(QAbstractItemView::NoEditTriggers);
    box->addWidget(mProblemTable, 1);

    QHBoxLayout* actions = new QHBoxLayout;
    mImportButton = new QPushButton(tr("Create"), page);
    mImportButton->setToolTip(tr("Download the statement with its sample cases, write the files and add the problem to the problem set"));
    mStatementButton = new QPushButton(tr("Statement"), page);
    mStatementButton->setToolTip(tr("Only show the statement, without writing files"));
    mRunButton = new QPushButton(tr("Test samples"), page);
    mRunButton->setToolTip(tr("Compile the current source and run it against every sample of the selected problem"));
    actions->addWidget(mImportButton);
    actions->addWidget(mStatementButton);
    actions->addWidget(mRunButton);
    box->addLayout(actions);

    connect(mRefreshClassButton, &QPushButton::clicked, this, &AscPanelWidget::onRefreshClasses);
    connect(mTaskKindCombo, QOverload<int>::of(&QComboBox::currentIndexChanged),
            this, &AscPanelWidget::onTaskKindChanged);
    connect(mLoadTaskButton, &QPushButton::clicked, this, &AscPanelWidget::onLoadTaskListClicked);
    connect(mOpenTaskButton, &QPushButton::clicked, this, &AscPanelWidget::onOpenTaskClicked);
    connect(mLoadTaskIdButton, &QPushButton::clicked, this, &AscPanelWidget::onLoadTaskByIdClicked);
    connect(mTaskIdEdit, &QLineEdit::returnPressed, this, &AscPanelWidget::onLoadTaskByIdClicked);
    connect(mImportButton, &QPushButton::clicked, this, &AscPanelWidget::onImportSelectedTaskProblem);
    connect(mStatementButton, &QPushButton::clicked, this, &AscPanelWidget::onOpenStatementClicked);
    connect(mRunButton, &QPushButton::clicked, this, &AscPanelWidget::onRunSamplesClicked);
    connect(mProblemTable, &QTableWidget::cellDoubleClicked,
            this, &AscPanelWidget::onProblemsDoubleClicked);

    return page;
}

QWidget* AscPanelWidget::buildBankPage()
{
    QWidget* page = new QWidget(this);
    QVBoxLayout* box = new QVBoxLayout(page);

    QHBoxLayout* row = new QHBoxLayout;
    mBankSearchEdit = new QLineEdit(page);
    mBankSearchEdit->setPlaceholderText(tr("Problem id or keyword"));
    mBankSearchButton = new QPushButton(tr("Search"), page);
    row->addWidget(mBankSearchEdit, 1);
    row->addWidget(mBankSearchButton);
    box->addLayout(row);

    mBankList = new QListWidget(page);
    box->addWidget(mBankList, 1);

    QHBoxLayout* actions = new QHBoxLayout;
    mBankImportButton = new QPushButton(tr("Create"), page);
    mBankImportButton->setToolTip(tr("Download the statement with its sample cases and add the problem to the problem set"));
    actions->addWidget(mBankImportButton);
    actions->addStretch();
    box->addLayout(actions);

    connect(mBankSearchButton, &QPushButton::clicked, this, &AscPanelWidget::onSearchBankClicked);
    connect(mBankSearchEdit, &QLineEdit::returnPressed, this, &AscPanelWidget::onSearchBankClicked);
    connect(mBankImportButton, &QPushButton::clicked, this, &AscPanelWidget::onImportSelectedBankProblem);
    connect(mBankList, &QListWidget::itemDoubleClicked,
            this, [this](QListWidgetItem*) { onImportSelectedBankProblem(); });

    return page;
}

QWidget* AscPanelWidget::buildStatementPage()
{
    QWidget* page = new QWidget(this);
    QVBoxLayout* box = new QVBoxLayout(page);
    mStatementView = new QTextBrowser(page);
    mStatementView->setOpenExternalLinks(true);
    box->addWidget(mStatementView);
    return page;
}

bool AscPanelWidget::teacherMode() const
{
    const QString role = mSession->session().role;
    return !role.isEmpty() && role != QLatin1String("student");
}

void AscPanelWidget::updateSessionState()
{
    const bool loggedIn = mSession->isLoggedIn();
    const bool busy = mSession->busy();

    mLoginButton->setEnabled(!loggedIn && !busy);
    mLogoutButton->setEnabled(loggedIn && !busy);
    mPhoneEdit->setEnabled(!loggedIn && !busy);
    mPasswordEdit->setEnabled(!loggedIn && !busy);
    mRefreshClassButton->setEnabled(loggedIn && !busy);
    mLoadTaskButton->setEnabled(loggedIn && !busy);
    mOpenTaskButton->setEnabled(loggedIn && mTaskCombo->count() > 0);
    mLoadTaskIdButton->setEnabled(loggedIn && !busy);
    mBankSearchButton->setEnabled(loggedIn);
    mBankImportButton->setEnabled(loggedIn);
    mImportButton->setEnabled(loggedIn);
    mStatementButton->setEnabled(loggedIn);

    if (busy) {
        mStatusLabel->setText(tr("Aisichuang: contacting the site ..."));
    } else if (loggedIn) {
        const AscSession session = mSession->session();
        mStatusLabel->setText(tr("Aisichuang: logged in as %1 (%2)")
                              .arg(Asc::maskAccount(session.phone), session.role));
    } else {
        mStatusLabel->setText(tr("Aisichuang: not logged in"));
    }

    if (!loggedIn) {
        mPasswordEdit->clear();
        if (mPhoneEdit->text().isEmpty())
            mPhoneEdit->setText(pSettings->aisichuang().lastPhone());
    }

    const bool teacher = teacherMode();
    mClassCombo->setVisible(teacher);
    mRefreshClassButton->setVisible(teacher);
    mLoadTaskButton->setVisible(teacher);
    mTaskCombo->setVisible(teacher);
    mOpenTaskButton->setVisible(teacher);
    mTaskKindCombo->setVisible(true);
}

void AscPanelWidget::setBusyIndicator(const QString& text)
{
    mStatusLabel->setText(text);
    mTabs->setEnabled(false);
}

void AscPanelWidget::clearBusyIndicator()
{
    mTabs->setEnabled(true);
    updateSessionState();
}

void AscPanelWidget::beginLogin()
{
    mTabs->setCurrentIndex(0);
    mPhoneEdit->setText(mSession->session().phone.isEmpty()
                        ? pSettings->aisichuang().lastPhone()
                        : mSession->session().phone);
    if (mPhoneEdit->text().isEmpty())
        mPhoneEdit->setFocus();
    else
        mPasswordEdit->setFocus();
}

void AscPanelWidget::onLoginClicked()
{
    const QString phone = mPhoneEdit->text().trimmed();
    const QString password = mPasswordEdit->text();
    if (phone.isEmpty() || password.isEmpty()) {
        emit errorMessage(tr("Enter both the phone number and the password."));
        return;
    }
    setBusyIndicator(tr("Logging in ..."));
    mSession->login(phone, password);
}

void AscPanelWidget::onLogoutClicked()
{
    mSession->logout();
    mClasses.clear();
    mTests.clear();
    mWorks.clear();
    mClassCombo->clear();
    mTaskCombo->clear();
    mProblemTable->setRowCount(0);
    mBankList->clear();
    mLoadedTaskId.clear();
    mLoadedTaskName.clear();
}

void AscPanelWidget::onRefreshClasses()
{
    if (!mSession->isLoggedIn())
        return;
    setBusyIndicator(tr("Loading classes ..."));
    QPointer<AscPanelWidget> guard(this);
    mClient->listClasses([guard](const QVector<AscClassInfo>& classes) {
        if (!guard) return;
        guard->clearBusyIndicator();
        guard->mClasses = classes;
        guard->mClassCombo->clear();
        for (const AscClassInfo& info : classes)
            guard->mClassCombo->addItem(info.className, info.classId);
        guard->updateSessionState();
        emit guard->statusMessage(classes.isEmpty()
                ? tr("Aisichuang: no class was returned for this account.")
                : tr("Aisichuang: %1 classes loaded.").arg(classes.count()));
    }, [guard](const QString& message) {
        if (!guard) return;
        guard->clearBusyIndicator();
        emit guard->errorMessage(message);
    });
}

void AscPanelWidget::onTaskKindChanged(int)
{
    mTaskCombo->clear();
    const bool isWork = mTaskKindCombo->currentIndex() == 1;
    const QVector<AscTaskInfo>& tasks = isWork ? mWorks : mTests;
    for (const AscTaskInfo& task : tasks) {
        QString label = task.taskName;
        if (task.problemCount > 0)
            label += QStringLiteral(" (%1)").arg(task.problemCount);
        mTaskCombo->addItem(label, task.taskId);
    }
    mOpenTaskButton->setEnabled(!tasks.isEmpty());
}

void AscPanelWidget::onLoadTaskListClicked()
{
    QString classId = mClassCombo->currentData().toString();
    if (classId.isEmpty()) {
        emit errorMessage(tr("Pick a class first; the class list needs a teacher account."));
        return;
    }
    const bool isWork = mTaskKindCombo->currentIndex() == 1;
    AscProblemSource source = isWork ? AscProblemSource::Work : AscProblemSource::Test;
    setBusyIndicator(isWork ? tr("Loading homework list ...") : tr("Loading test list ..."));

    const QString className = mClassCombo->currentText();
    QPointer<AscPanelWidget> guard(this);
    mClient->listTasks(source, classId, [guard, source, classId, className](const QVector<AscTaskInfo>& tasks) {
        if (!guard) return;
        guard->clearBusyIndicator();
        if (source == AscProblemSource::Work)
            guard->mWorks = tasks;
        else
            guard->mTests = tasks;
        guard->mLoadedClassId = classId;
        guard->mLoadedClassName = className;
        guard->onTaskKindChanged(guard->mTaskKindCombo->currentIndex());
        emit guard->statusMessage(tr("Aisichuang: %1 tasks loaded.").arg(tasks.count()));
    }, [guard](const QString& message) {
        if (!guard) return;
        guard->clearBusyIndicator();
        emit guard->errorMessage(message);
    });
}

void AscPanelWidget::onOpenTaskClicked()
{
    const QString taskId = mTaskCombo->currentData().toString();
    if (taskId.isEmpty())
        return;
    const bool isWork = mTaskKindCombo->currentIndex() == 1;
    mLoadedTaskName = mTaskCombo->currentText();
    mTaskIdEdit->setText(taskId);
    loadProblemsOfTask(taskId, isWork ? AscProblemSource::Work : AscProblemSource::Test);
}

void AscPanelWidget::onLoadTaskByIdClicked()
{
    const QString taskId = mTaskIdEdit->text().trimmed();
    bool ok = false;
    taskId.toLongLong(&ok);
    if (taskId.isEmpty() || !ok) {
        emit errorMessage(tr("The test id / work id has to be a number."));
        return;
    }
    const bool isWork = mTaskKindCombo->currentIndex() == 1;
    mLoadedTaskName = QStringLiteral("%1 %2")
            .arg(isWork ? tr("Homework") : tr("Classroom test"), taskId);
    loadProblemsOfTask(taskId, isWork ? AscProblemSource::Work : AscProblemSource::Test);
}

void AscPanelWidget::loadProblemsOfTask(const QString& taskId, AscProblemSource source)
{
    if (!mSession->isLoggedIn())
        return;
    mLoadedTaskId = taskId;
    mLoadedSource = source;
    setBusyIndicator(tr("Loading the problems of task %1 ...").arg(taskId));

    QPointer<AscPanelWidget> guard(this);
    mClient->listTaskProblems(source, taskId, [guard, taskId](const QVector<AscTaskProblem>& problems) {
        if (!guard) return;
        guard->clearBusyIndicator();
        guard->populateProblemTable(problems);
        emit guard->statusMessage(tr("Aisichuang: task %1 has %2 programming problems.")
                                  .arg(taskId).arg(problems.count()));
    }, [guard](const QString& message) {
        if (!guard) return;
        guard->clearBusyIndicator();
        emit guard->errorMessage(message);
    });
}

void AscPanelWidget::populateProblemTable(const QVector<AscTaskProblem>& problems)
{
    mProblemTable->setRowCount(problems.count());
    for (int row = 0; row < problems.count(); ++row) {
        const AscTaskProblem& problem = problems[row];
        mProblemTable->setItem(row, 0, new QTableWidgetItem(problem.displayId));
        mProblemTable->setItem(row, 1, new QTableWidgetItem(problem.title));
        mProblemTable->setItem(row, 2, new QTableWidgetItem(problem.done ? tr("Yes") : tr("No")));
    }
    if (!problems.isEmpty())
        mProblemTable->selectRow(0);
    else
        mProblemTable->setCurrentCell(-1, -1);
}

AscImportContext AscPanelWidget::currentContext() const
{
    AscImportContext context;
    context.source = mLoadedSource;
    context.taskId = mLoadedTaskId;
    context.taskName = mLoadedTaskName;
    context.classId = mLoadedClassId;
    context.className = mLoadedClassName;
    return context;
}

void AscPanelWidget::onImportSelectedTaskProblem()
{
    const int row = mProblemTable->currentRow();
    if (row < 0) {
        emit errorMessage(tr("Select a problem in the list first."));
        return;
    }
    importDisplayId(mProblemTable->item(row, 0)->text(),
                    mProblemTable->item(row, 1)->text(),
                    currentContext());
}

void AscPanelWidget::onProblemsDoubleClicked(int row, int)
{
    if (row < 0)
        return;
    mProblemTable->selectRow(row);
    onImportSelectedTaskProblem();
}

void AscPanelWidget::onOpenStatementClicked()
{
    const int row = mProblemTable->currentRow();
    if (row < 0) {
        emit errorMessage(tr("Select a problem in the list first."));
        return;
    }
    const QString displayId = mProblemTable->item(row, 0)->text();
    const QString title = mProblemTable->item(row, 1)->text();
    setBusyIndicator(tr("Fetching the statement of %1 ...").arg(displayId));
    QPointer<AscPanelWidget> guard(this);
    mClient->fetchStatement(displayId, title, [guard](const AscStatement& statement) {
        if (!guard) return;
        guard->clearBusyIndicator();
        guard->showStatement(statement);
    }, [guard, displayId](const QString& message) {
        if (!guard) return;
        guard->clearBusyIndicator();
        emit guard->errorMessage(tr("Problem %1: %2").arg(displayId, message));
    });
}

void AscPanelWidget::onImportSelectedBankProblem()
{
    QListWidgetItem* item = mBankList->currentItem();
    if (!item) {
        emit errorMessage(tr("Pick a problem from the search result."));
        return;
    }
    AscImportContext context;
    context.source = AscProblemSource::Bank;
    importDisplayId(item->data(Qt::UserRole).toString(),
                    item->data(Qt::UserRole + 1).toString(), context);
}

QString AscPanelWidget::problemFolder(AscProblemSource source, const QString& taskName)
{
    QString base = pSettings->aisichuang().taskRootDir();
    if (base.isEmpty())
        base = QDir::homePath() + QStringLiteral("/aisichuang");
    switch (source) {
    case AscProblemSource::Test:
        return getFilePath(getFilePath(base, QStringLiteral("班测")),
                           AscProblemImporter::sanitizeName(taskName));
    case AscProblemSource::Work:
        return getFilePath(getFilePath(base, QStringLiteral("作业")),
                           AscProblemImporter::sanitizeName(taskName));
    case AscProblemSource::Bank:
        break;
    }
    return getFilePath(base, QStringLiteral("题库"));
}

void AscPanelWidget::importDisplayId(const QString& displayId, const QString& title,
                                     const AscImportContext& context)
{
    if (displayId.isEmpty())
        return;
    mImporter->importProblem(displayId, title, context,
                             problemFolder(context.source, context.taskName),
                             pSettings->aisichuang().codeTemplate(),
                             pSettings->aisichuang().saveStatementAsMarkdown());
}

void AscPanelWidget::onSearchBankClicked()
{
    const QString keyword = mBankSearchEdit->text().trimmed();
    if (keyword.isEmpty())
        return;
    if (!mSession->isLoggedIn()) {
        emit errorMessage(tr("Log in first."));
        return;
    }
    setBusyIndicator(tr("Searching the question bank ..."));
    QPointer<AscPanelWidget> guard(this);
    mClient->searchProblems(keyword, 1,
            [guard](const QVector<AscProblemRef>& problems, qlonglong total) {
        if (!guard) return;
        guard->clearBusyIndicator();
        guard->mBankList->clear();
        for (const AscProblemRef& problem : problems) {
            QListWidgetItem* item = new QListWidgetItem(
                    QStringLiteral("%1  %2").arg(problem.displayId, problem.title).trimmed(),
                    guard->mBankList);
            item->setData(Qt::UserRole, problem.displayId);
            item->setData(Qt::UserRole + 1, problem.title);
            item->setToolTip(tr("Internal id %1").arg(problem.problemId));
        }
        emit guard->statusMessage(tr("Aisichuang: %1 of %2 bank problems shown.")
                                  .arg(problems.count()).arg(total));
    }, [guard](const QString& message) {
        if (!guard) return;
        guard->clearBusyIndicator();
        emit guard->errorMessage(message);
    });
}

void AscPanelWidget::showStatement(const AscStatement& statement)
{
    QString markdown = statement.rawMarkdown;
    if (markdown.isEmpty())
        markdown = Asc::statementToMarkdown(statement);
    QString header = QStringLiteral("# %1 %2\n\n").arg(statement.displayId, statement.title).trimmed()
            + QStringLiteral("\n");
#if QT_VERSION >= QT_VERSION_CHECK(5, 14, 0)
    mStatementView->setMarkdown(header + markdown);
#else
    mStatementView->setPlainText(header + markdown);
#endif
    mTabs->setCurrentIndex(3);
}

void AscPanelWidget::onRunSamplesClicked()
{
    emit runSamplesRequested();
}
