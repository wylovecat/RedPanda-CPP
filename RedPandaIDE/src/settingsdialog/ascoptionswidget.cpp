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
#include "ascoptionswidget.h"

#include "../aisichuang/ascclient.h"
#include "../mainwindow.h"
#include "../settings.h"

#include <QCheckBox>
#include <QComboBox>
#include <QFileDialog>
#include <QFormLayout>
#include <QGroupBox>
#include <QHBoxLayout>
#include <QLabel>
#include <QLineEdit>
#include <QPlainTextEdit>
#include <QPushButton>
#include <QSpinBox>
#include <QVBoxLayout>

AscOptionsWidget::AscOptionsWidget(const QString& name, const QString& group,
                                   IconsManager* iconsManager, QWidget* parent):
    SettingsWidget(name, group, iconsManager, parent)
{
    QVBoxLayout* layout = new QVBoxLayout(this);

    QGroupBox* connectionBox = new QGroupBox(tr("Connection"), this);
    QFormLayout* connectionForm = new QFormLayout(connectionBox);
    mBaseUrlEdit = new QLineEdit(connectionBox);
    mBaseUrlEdit->setPlaceholderText(QStringLiteral("https://www.aisichuang.net/tiku"));
    connectionForm->addRow(tr("API root"), mBaseUrlEdit);
    mThrottleSpin = new QSpinBox(connectionBox);
    mThrottleSpin->setRange(0, 2000);
    mThrottleSpin->setSingleStep(50);
    mThrottleSpin->setSuffix(QStringLiteral(" ms"));
    mThrottleSpin->setToolTip(tr("Minimum delay between two requests; the site dislikes parallel calls."));
    connectionForm->addRow(tr("Request spacing"), mThrottleSpin);
    layout->addWidget(connectionBox);

    QGroupBox* folderBox = new QGroupBox(tr("Downloaded problems"), this);
    QVBoxLayout* folderLayout = new QVBoxLayout(folderBox);
    QHBoxLayout* folderRow = new QHBoxLayout;
    mFolderEdit = new QLineEdit(folderBox);
    QPushButton* browseButton = new QPushButton(tr("Browse"), folderBox);
    folderRow->addWidget(mFolderEdit, 1);
    folderRow->addWidget(browseButton);
    folderLayout->addLayout(folderRow);
    mSaveStatementCheck = new QCheckBox(tr("Save the statement as a Markdown file"), folderBox);
    folderLayout->addWidget(mSaveStatementCheck);
    QLabel* folderHint = new QLabel(tr("Every problem is written to its own folder as "
                                       "<id>.cpp plus <id>_1.in / <id>_1.out sample files."),
                                    folderBox);
    folderHint->setWordWrap(true);
    folderLayout->addWidget(folderHint);
    layout->addWidget(folderBox);

    QGroupBox* templateBox = new QGroupBox(tr("Code template for a new problem"), this);
    QVBoxLayout* templateLayout = new QVBoxLayout(templateBox);
    mTemplateEdit = new QPlainTextEdit(templateBox);
    mTemplateEdit->setLineWrapMode(QPlainTextEdit::NoWrap);
    templateLayout->addWidget(mTemplateEdit);
    layout->addWidget(templateBox, 1);

    QGroupBox* submitBox = new QGroupBox(tr("Submitting to the site"), this);
    QFormLayout* submitForm = new QFormLayout(submitBox);
    mEnableSubmitCheck = new QCheckBox(tr("Allow submitting code to aisichuang.net"), submitBox);
    mEnableSubmitCheck->setToolTip(tr("Off by default: this writes a real answer into the "
                                      "teacher's class, and it cannot be undone from here."));
    submitForm->addRow(mEnableSubmitCheck);
    mLanguageCombo = new QComboBox(submitBox);
    mLanguageCombo->setEditable(true);
    mLanguageCombo->addItem(QStringLiteral("C++"));
    mLanguageCombo->addItem(QStringLiteral("C"));
    submitForm->addRow(tr("Language"), mLanguageCombo);
    mPollIntervalSpin = new QSpinBox(submitBox);
    mPollIntervalSpin->setRange(500, 20000);
    mPollIntervalSpin->setSingleStep(500);
    mPollIntervalSpin->setSuffix(QStringLiteral(" ms"));
    submitForm->addRow(tr("Result polling interval"), mPollIntervalSpin);
    mPollTimeoutSpin = new QSpinBox(submitBox);
    mPollTimeoutSpin->setRange(5000, 600000);
    mPollTimeoutSpin->setSingleStep(5000);
    mPollTimeoutSpin->setSuffix(QStringLiteral(" ms"));
    submitForm->addRow(tr("Give up after"), mPollTimeoutSpin);
    layout->addWidget(submitBox);

    connect(browseButton, &QPushButton::clicked, this, &AscOptionsWidget::onBrowseFolder);
}

void AscOptionsWidget::onBrowseFolder()
{
    QString folder = QFileDialog::getExistingDirectory(this, tr("Problem Folder"),
                                                      mFolderEdit->text());
    if (!folder.isEmpty())
        mFolderEdit->setText(folder);
}

void AscOptionsWidget::doLoad()
{
    const AscSettings& settings = pSettings->aisichuang();
    mBaseUrlEdit->setText(settings.baseUrl());
    mThrottleSpin->setValue(settings.throttleMs());
    mFolderEdit->setText(settings.taskRootDir());
    mSaveStatementCheck->setChecked(settings.saveStatementAsMarkdown());
    mTemplateEdit->setPlainText(settings.codeTemplate());
    mEnableSubmitCheck->setChecked(settings.enableOnlineSubmit());
    mLanguageCombo->setCurrentText(settings.submitLanguage());
    mPollIntervalSpin->setValue(settings.pollIntervalMs());
    mPollTimeoutSpin->setValue(settings.pollTimeoutMs());

    // When a session is alive, offer exactly the names the site accepts.
    const QStringList languages = pMainWindow->aisichuangLanguages();
    if (!languages.isEmpty()) {
        mLanguageCombo->clear();
        mLanguageCombo->addItems(languages);
        mLanguageCombo->setCurrentText(settings.submitLanguage());
    }
}

void AscOptionsWidget::doSave()
{
    AscSettings& settings = pSettings->aisichuang();
    settings.setBaseUrl(mBaseUrlEdit->text().trimmed());
    settings.setThrottleMs(mThrottleSpin->value());
    settings.setTaskRootDir(mFolderEdit->text().trimmed());
    settings.setSaveStatementAsMarkdown(mSaveStatementCheck->isChecked());
    settings.setCodeTemplate(mTemplateEdit->toPlainText());
    settings.setEnableOnlineSubmit(mEnableSubmitCheck->isChecked());
    settings.setSubmitLanguage(mLanguageCombo->currentText().trimmed());
    settings.setPollIntervalMs(mPollIntervalSpin->value());
    settings.setPollTimeoutMs(mPollTimeoutSpin->value());
    settings.save();
    pMainWindow->applyAisichuangSettings();
}
