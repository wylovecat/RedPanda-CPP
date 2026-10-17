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
#include "imageexporter.h"

#include "../document.h"
#include <QObject>
#include <qt_utils/utils.h>

namespace QSynedit {

ImageExporter::ImageExporter(int tabSize, const QByteArray& charset):
    Exporter(tabSize, charset),
    mPainter{nullptr},
    mFM{mFont},
    mAscent{mFM.ascent()},
    mLineHeight{mFM.lineSpacing()},
    mX{0},
    mY{0},
    mColumn{0},
    mLineNumber{0},
    mPadding{6},
    mBorderWidth{1},
    mGutterWidth{0},
    mGutterPadding{8},
    mCodeLeftPadding{6},
    mCodeStartX{0},
    mBorderColor{QColor{0xa0, 0xa0, 0xa0}},
    mSeparatorColor{},
    mCurrentFG{mForegroundColor},
    mCurrentBG{mBackgroundColor},
    mShowLineNumbers{true}
{
    mDefaultFilter = "PNG Image (*.png)";
}

void ImageExporter::saveToImage(const std::shared_ptr<const Document>& doc, const QString& filename)
{
    const QFontMetrics baseFM(mFont);
    mLineHeight = baseFM.lineSpacing();
    mAscent = baseFM.ascent();

    const int lineCount = doc ? doc->count() : 0;

    // Resolve auto-derived colors.
    QColor gutterBG = mGutterBG;
    if (!gutterBG.isValid()) {
        if (mBackgroundColor.lightness() >= 128)
            gutterBG = mBackgroundColor.darker(106);
        else
            gutterBG = mBackgroundColor.lighter(115);
    }
    QColor gutterFG = mGutterFG;
    if (!gutterFG.isValid()) {
        gutterFG = mForegroundColor;
        gutterFG.setAlpha(150);
    }
    QColor separatorColor = mSeparatorColor;
    if (!separatorColor.isValid())
        separatorColor = mBorderColor;

    // Gutter width is sized for the widest line number.
    if (mShowLineNumbers) {
        const int lastNumber = lineNumberStartFromZero() ? (lineCount > 0 ? lineCount-1 : 0) : lineCount;
        mGutterWidth = baseFM.horizontalAdvance(QString::number(lastNumber)) + 2*mGutterPadding;
    } else {
        mGutterWidth = 0;
    }
    const int separatorWidth = mShowLineNumbers ? 1 : 0;
    mCodeStartX = mBorderWidth + mGutterWidth + separatorWidth + mCodeLeftPadding;

    // Measure the widest code line (expanding tabs to the configured tab stops).
    int maxWidth = 0;
    for (int i=0; i<lineCount; i++) {
        const QString line = doc->getLine(i);
        int w = 0;
        int col = 0;
        for (int j=0; j<line.length(); j++) {
            const QChar ch = line[j];
            if (ch == QChar('\t')) {
                const int nextStop = ((col/mTabSize)+1)*mTabSize;
                w += baseFM.horizontalAdvance(QChar(' ')) * (nextStop-col);
                col = nextStop;
            } else {
                w += baseFM.horizontalAdvance(ch);
                col++;
            }
        }
        if (w > maxWidth)
            maxWidth = w;
    }

    int width = mBorderWidth + mGutterWidth + separatorWidth + mCodeLeftPadding + maxWidth + mPadding + mBorderWidth;
    int height = lineCount*mLineHeight + 2*mPadding + 2*mBorderWidth;
    if (width < 1)
        width = 1;
    if (height < 1)
        height = 1;

    mImage = QImage(width, height, QImage::Format_ARGB32_Premultiplied);
    mImage.fill(mBackgroundColor);

    QPainter painter(&mImage);
    painter.setRenderHint(QPainter::TextAntialiasing, true);

    if (mShowLineNumbers) {
        painter.fillRect(mBorderWidth, mBorderWidth, mGutterWidth, height-2*mBorderWidth, gutterBG);
        QPen sepPen(separatorColor);
        sepPen.setWidth(separatorWidth);
        painter.setPen(sepPen);
        const int sepX = mBorderWidth + mGutterWidth;
        painter.drawLine(sepX, mBorderWidth, sepX, height-mBorderWidth-1);
    }

    if (mBorderWidth > 0) {
        QPen pen(mBorderColor);
        pen.setWidth(mBorderWidth);
        painter.setPen(pen);
        painter.setBrush(Qt::NoBrush);
        painter.drawRect(0, 0, width-1, height-1);
    }

    mGutterBG = gutterBG;
    mGutterFG = gutterFG;
    mSeparatorColor = separatorColor;

    mPainter = &painter;
    mX = mCodeStartX;
    mY = mBorderWidth + mPadding;
    mColumn = 0;
    mLineNumber = 0;
    mCurrentFont = mFont;
    mFM = QFontMetrics(mFont);
    mAscent = mFM.ascent();
    mCurrentFG = mForegroundColor;
    mCurrentBG = mBackgroundColor;
    if (mShowLineNumbers)
        drawLineNumber(0);

    exportAll(doc);

    painter.end();
    mPainter = nullptr;

    if (!mImage.save(filename)) {
        throw FileError(QObject::tr("Can't save image to '%1'!").arg(filename));
    }
}

void ImageExporter::drawLineNumber(int index)
{
    if (!mPainter || !mShowLineNumbers)
        return;
    const int number = lineNumberStartFromZero() ? index : index+1;
    const QString text = QString::number(number);
    mPainter->setFont(mFont);
    mPainter->setPen(mGutterFG);
    const int textWidth = QFontMetrics(mFont).horizontalAdvance(text);
    // Right-align the number, sharing the code baseline.
    const int x = mBorderWidth + mGutterWidth - mGutterPadding - textWidth;
    const int y = mBorderWidth + mPadding + index*mLineHeight + mAscent;
    mPainter->drawText(x, y, text);
}

void ImageExporter::setTokenAttribute(PTokenAttribute attri)
{
    mCurrentFG = attri->foreground();
    mCurrentBG = attri->background();
    mCurrentFont = mFont;
    if (attri->styles().testFlag(FontStyle::fsBold))
        mCurrentFont.setBold(true);
    if (attri->styles().testFlag(FontStyle::fsItalic))
        mCurrentFont.setItalic(true);
    if (attri->styles().testFlag(FontStyle::fsUnderline))
        mCurrentFont.setUnderline(true);
    if (attri->styles().testFlag(FontStyle::fsStrikeOut))
        mCurrentFont.setStrikeOut(true);
    mFM = QFontMetrics(mCurrentFont);
    mAscent = mFM.ascent();
    Exporter::setTokenAttribute(attri);
}

void ImageExporter::formatToken(const QString& token)
{
    if (!mPainter || token.isEmpty())
        return;
    const QColor fg = mCurrentFG.isValid() ? mCurrentFG : mForegroundColor;
    const QColor bg = (mUseBackground && mCurrentBG.isValid()) ? mCurrentBG : QColor();

    mPainter->setFont(mCurrentFont);
    const int len = token.length();
    int start = 0;
    for (int i=0; i<=len; i++) {
        if (i == len || token[i] == QChar('\t')) {
            if (i > start) {
                const QString segment = token.mid(start, i-start);
                const int w = mFM.horizontalAdvance(segment);
                if (bg.isValid())
                    mPainter->fillRect(mX, mY, w, mLineHeight, bg);
                mPainter->setPen(fg);
                mPainter->drawText(mX, mY + mAscent, segment);
                mX += w;
                mColumn += segment.length();
            }
            if (i < len) { // tab: advance to the next tab stop
                const int nextStop = ((mColumn/mTabSize)+1)*mTabSize;
                const int w = mFM.horizontalAdvance(QChar(' ')) * (nextStop-mColumn);
                if (bg.isValid())
                    mPainter->fillRect(mX, mY, w, mLineHeight, bg);
                mX += w;
                mColumn = nextStop;
            }
            start = i+1;
        }
    }
}

void ImageExporter::formatNewLine()
{
    mY += mLineHeight;
    mX = mCodeStartX;
    mColumn = 0;
    mLineNumber++;
    if (mShowLineNumbers)
        drawLineNumber(mLineNumber);
}

void ImageExporter::formatAttributeDone(bool, bool, FontStyles)
{
}

void ImageExporter::formatAttributeInit(bool, bool, FontStyles)
{
}

void ImageExporter::formatAfterLastAttribute()
{
}

void ImageExporter::formatBeforeFirstAttribute(bool, bool, FontStyles)
{
}

QString ImageExporter::getFooter()
{
    return QString();
}

QString ImageExporter::getFormatName()
{
    return "Image";
}

QString ImageExporter::getHeader()
{
    return QString();
}

}
