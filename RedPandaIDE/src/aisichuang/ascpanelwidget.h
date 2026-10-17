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
#ifndef ASCPANELWIDGET_H
#define ASCPANELWIDGET_H

#include "ascproblemimporter.h"
#include "asctypes.h"

#include <QPointer>
#include <QWidget>

class AscClient;
class AscSessionManager;
class QComboBox;
class QLineEdit;
class QListWidget;
class QPushButton;
class QLabel;
class QTableWidget;
class QTextBrowser;
class QTabWidget;

/**
 * The aisichuang page of the explorer dock.
 *
 * It is deliberately laid out in code: the content is a set of async results
 * (classes, tests, homework, bank search hits, problem rows) that all
 * repopulate widgets, and the three groups never share a fixed geometry.
 *
 * Nothing here judges code by itself: importing a problem hands an OJProblem
 * to the IDE's problem set, and "Test with samples" asks the main window to
 * run the cases it already knows how to run.
 */
class AscPanelWidget: public QWidget
{
    Q_OBJECT
public:
    explicit AscPanelWidget(AscClient* client, AscSessionManager* session,
                            AscProblemImporter* importer, QWidget* parent = nullptr);

    /** Reflect a login/logout/busy change coming from the session manager. */
    void updateSessionState();

    /** Show a problem statement in the "Statement" tab. */
    void showStatement(const AscStatement& statement);

    /**
     * Where a problem is written: "<root>/题库" for the bank, and a folder per
     * test or homework otherwise. The root and template come from the options
     * page, which keeps this the only place the download layout is decided.
     */
    static QString problemFolder(AscProblemSource source, const QString& taskName);

public slots:
    /** Pre-fill the phone field and focus the password, used by the login action. */
    void beginLogin();

signals:
    void statusMessage(const QString& message);
    void errorMessage(const QString& message);
    /** A problem was imported; the main window adds it to the problem set. */
    void problemImported(const AscImportedProblem& imported);
    /** Ask the main window to compile and run every sample of the selected problem. */
    void runSamplesRequested();

private slots:
    void onLoginClicked();
    void onLogoutClicked();
    void onRefreshClasses();
    void onTaskKindChanged(int index);
    void onLoadTaskListClicked();
    void onOpenTaskClicked();
    void onLoadTaskByIdClicked();
    void onSearchBankClicked();
    void onImportSelectedTaskProblem();
    void onImportSelectedBankProblem();
    void onOpenStatementClicked();
    void onRunSamplesClicked();
    void onProblemsDoubleClicked(int row, int column);

private:
    QWidget* buildAccountPage();
    QWidget* buildTaskPage();
    QWidget* buildBankPage();
    QWidget* buildStatementPage();

    void setBusyIndicator(const QString& text);
    void clearBusyIndicator();
    AscImportContext currentContext() const;
    void loadProblemsOfTask(const QString& taskId, AscProblemSource source);
    void populateProblemTable(const QVector<AscTaskProblem>& problems);
    void importDisplayId(const QString& displayId, const QString& title,
                         const AscImportContext& context);
    QString selectedTaskId() const;
    bool teacherMode() const;

private:
    AscClient* mClient;
    AscSessionManager* mSession;
    AscProblemImporter* mImporter;

    QTabWidget* mTabs;
    QLabel* mStatusLabel;
    QLineEdit* mPhoneEdit;
    QLineEdit* mPasswordEdit;
    QPushButton* mLoginButton;
    QPushButton* mLogoutButton;

    QComboBox* mClassCombo;
    QPushButton* mRefreshClassButton;
    QComboBox* mTaskKindCombo;
    QPushButton* mLoadTaskButton;
    QComboBox* mTaskCombo;
    QPushButton* mOpenTaskButton;
    QLineEdit* mTaskIdEdit;
    QPushButton* mLoadTaskIdButton;
    QTableWidget* mProblemTable;
    QPushButton* mImportButton;
    QPushButton* mStatementButton;
    QPushButton* mRunButton;

    QLineEdit* mBankSearchEdit;
    QPushButton* mBankSearchButton;
    QListWidget* mBankList;
    QPushButton* mBankImportButton;

    QTextBrowser* mStatementView;

    QVector<AscClassInfo> mClasses;
    QVector<AscTaskInfo> mTests;
    QVector<AscTaskInfo> mWorks;
    QString mLoadedTaskId;
    QString mLoadedTaskName;
    AscProblemSource mLoadedSource;
    QString mLoadedClassName;
    QString mLoadedClassId;
};

#endif // ASCPANELWIDGET_H
