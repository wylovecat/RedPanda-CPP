/*
 * Copyright (C) 2020-2022 Roy Qu (royqh1979@gmail.com)
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
#ifndef QSYNEDIT_IMAGEEXPORTER_H
#define QSYNEDIT_IMAGEEXPORTER_H

#include "exporter.h"

#include <QImage>
#include <QPainter>
#include <QFont>
#include <QFontMetrics>

namespace QSynedit {

class ImageExporter : public Exporter
{
public:
    explicit ImageExporter(int tabSize, const QByteArray& charset);
    ImageExporter(const ImageExporter&)=delete;
    ImageExporter& operator=(const ImageExporter&)=delete;

    /**
     * @brief Renders the whole document into an image using the syntaxer's
     *        token attributes, and saves it to filename. The image format is
     *        inferred from the file name extension (PNG by default).
     */
    void saveToImage(const std::shared_ptr<const Document>& doc, const QString& filename);

    const QImage& image() const { return mImage; }

    int padding() const { return mPadding; }
    void setPadding(int value) { mPadding = value; }

    int borderWidth() const { return mBorderWidth; }
    void setBorderWidth(int value) { mBorderWidth = value; }

    QColor borderColor() const { return mBorderColor; }
    void setBorderColor(const QColor& value) { mBorderColor = value; }

    bool showLineNumbers() const { return mShowLineNumbers; }
    void setShowLineNumbers(bool value) { mShowLineNumbers = value; }

    // The following colors are auto-derived from the background/foreground
    // color when left invalid.
    QColor gutterBackgroundColor() const { return mGutterBG; }
    void setGutterBackgroundColor(const QColor& value) { mGutterBG = value; }

    QColor gutterTextColor() const { return mGutterFG; }
    void setGutterTextColor(const QColor& value) { mGutterFG = value; }

    QColor separatorColor() const { return mSeparatorColor; }
    void setSeparatorColor(const QColor& value) { mSeparatorColor = value; }

protected:
    // SynExporter interface
    void formatAttributeDone(bool backgroundChanged, bool foregroundChanged, FontStyles fontStyles) override;
    void formatAttributeInit(bool backgroundChanged, bool foregroundChanged, FontStyles fontStyles) override;
    void formatAfterLastAttribute() override;
    void formatBeforeFirstAttribute(bool backgroundChanged, bool foregroundChanged, FontStyles fontStyles) override;
    void formatToken(const QString& token) override;
    void formatNewLine() override;
    void setTokenAttribute(PTokenAttribute attri) override;
    QString getFooter() override;
    QString getFormatName() override;
    QString getHeader() override;

private:
    void drawLineNumber(int index);

    QImage mImage;
    QPainter* mPainter;
    QFont mCurrentFont;
    QFontMetrics mFM;
    int mAscent;
    int mLineHeight;
    int mX;
    int mY;
    int mColumn;
    int mLineNumber;
    int mPadding;
    int mBorderWidth;
    int mGutterWidth;
    int mGutterPadding;
    int mCodeLeftPadding;
    int mCodeStartX;
    QColor mBorderColor;
    QColor mGutterBG;
    QColor mGutterFG;
    QColor mSeparatorColor;
    QColor mCurrentFG;
    QColor mCurrentBG;
    bool mShowLineNumbers;
};

}
#endif // QSYNEDIT_IMAGEEXPORTER_H
