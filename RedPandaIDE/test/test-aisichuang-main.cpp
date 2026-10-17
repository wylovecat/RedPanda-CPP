#include <QApplication>
#include <QTest>
#include "test_aisichuang.h"

int main(int argc, char *argv[]) {
    int status = 0;
    QTest::setMainSourcePath(__FILE__, QT_TESTCASE_BUILDDIR);

    // A QApplication (not CoreApplication) because the client's network stack
    // and the ssl backend are happier with the widget platform plugins loaded.
    QApplication app(argc, argv);
    {
        TestAisichuang tc;
        status |= QTest::qExec(&tc, argc, argv);
    }

    return status;
}
