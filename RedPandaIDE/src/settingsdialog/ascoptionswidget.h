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
#ifndef ASCOPTIONSWIDGET_H
#define ASCOPTIONSWIDGET_H

#include "settingswidget.h"

class QCheckBox;
class QComboBox;
class QLineEdit;
class QPlainTextEdit;
class QSpinBox;

/**
 * Options - Aisichuang page: where downloaded problems go, how polite the
 * polling is, and the switch that lets code be written back to the site.
 */
class AscOptionsWidget: public SettingsWidget
{
    Q_OBJECT
public:
    explicit AscOptionsWidget(const QString& name, const QString& group,
                              IconsManager* iconsManager, QWidget* parent = nullptr);

protected:
    void doLoad() override;
    void doSave() override;

private slots:
    void onBrowseFolder();

private:
    QLineEdit* mBaseUrlEdit;
    QLineEdit* mFolderEdit;
    QCheckBox* mSaveStatementCheck;
    QPlainTextEdit* mTemplateEdit;
    QCheckBox* mEnableSubmitCheck;
    QComboBox* mLanguageCombo;
    QSpinBox* mPollIntervalSpin;
    QSpinBox* mPollTimeoutSpin;
    QSpinBox* mThrottleSpin;
};

#endif // ASCOPTIONSWIDGET_H
