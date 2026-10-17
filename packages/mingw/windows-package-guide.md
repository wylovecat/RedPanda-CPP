# RedPandaIDE 爱思创版 · Windows 安装包构建说明

（这份文档与脚本也在坚果云共享目录里放了一份，内容一致。）

分支：`feature/aisichuang`（已推到 `wylovecat/RedPanda-CPP`）
产物：`redpanda-cpp-3.5.0-beta.2-ws2003_x64-none.exe`（NSIS 安装包）＋同名 `.7z`（免安装便携版）

先说清楚一件事：这条 Windows 构建路径我**没有在本机跑过**，本机只有 Qt5/Linux 工具链，能做且已经做的是 Linux 构建与全部自动化测试（`ctest` 4/4，含 19 项爱思创契约用例，其中真机登录/班级/作业/题面样例全绿）。下面这套命令是照仓库自带的 `packages/mingw/build-xp.sh` 和它自己的 CI 工作流 `build-app.yml`（`windows_legacy` job，用的就是 `-p 64-ucrt --ucrt`）逐项核对写出来的，不是我猜的，但最终得在你的 Windows 上过一遍。

## 1. 装 MSYS2

https://www.msys2.org 按默认路径装好。之后所有命令都在开始菜单的 **「MSYS2 MSYS」** 终端里执行——不是 CMD、不是 PowerShell、不是 Git Bash（脚本里的 `/mingw64/bin/cmake`、`/mingw32/bin/makensis` 只有在这个环境下才存在，脚本第一步也会拦住你）。

磁盘留 15 GB 以上（源码 + Qt + 构建中间产物 + NSIS 打包）。

## 2. 跑一键脚本

本仓库里的 [`packages/mingw/build-windows-package.sh`](build-windows-package.sh) 会做：拉 `feature/aisichuang` 分支 → 装依赖 → 编译 → NSIS 打包 → 把 `exe` 和 `7z` 拷回本文件夹（坚果云会自动同步）。

```bash
bash packages/mingw/build-windows-package.sh   # 在仓库根目录下执行
```

坚果云在你机器上的实际路径可能不是 `D:\Nutstore Files\...`，用右键文件夹「打开所在目录」确认一下再替换路径。

需要内置 MinGW64 编译器（目标机上没有任何编译器时）：

```bash
WITH_COMPILER=1 bash packages/mingw/build-windows-package.sh
```

GitHub 下不动时挂代理（Qt、工具链、astyle 分别从 GitHub / GitLab / Visual Studio 下载）：

```bash
export https_proxy=http://127.0.0.1:7890
```

想改 32 位老机器版：`PROFILE=32-ucrt`（产物名会变 `winxp_x86`）。

## 3. 装到学生机之后怎么验收

装完打开小熊猫C++，按这几步点一遍就是本次移植的全部功能：

1. 左侧资源管理器面板多了一个 **「爱思创」** 页；右下角状态栏有常驻的 **「爱思创: 未登录」**。
2. 账户页填手机号+密码 → 登录。状态变成「已登录 138\*\*\*\*34（teacher）」。密码不会被保存，只存站点发的令牌；令牌过期会提示重新登录。
3. 任务页：教师先点「刷新」拉班级，选班后点「拉列表」再点「打开」；学生直接在框里填班测/作业编号点「载入」。列表只显示编程题。
4. 选中一题点 **「建题」**：写出 `A0765.cpp` + `A0765_1.in/.out` + `A0765.md`，同时进题单并自动打开源码。目录默认 `~/aisichuang/题库|班测/<任务名>|作业/<任务名>/`，可在 **选项 → 爱思创** 改。已存在的 `.cpp` 不会被覆盖。
5. 写代码后点面板的 **「样例测评」** 或按 **`Ctrl+F6`**：走的是 IDE 原有的「运行所有测试点」，底部「题目」页看逐样例 Pass/Fail、进度与首处差异。
6. 题库页可以按题号/关键词搜索并建题；题面页用站点自己的 markdown 渲染。
7. **提交到站点默认关闭**：要用的话去 选项 → 爱思创 勾选「允许向爱思创提交代码」，然后 `Ctrl+Alt+S`，会再弹一次确认。它会真的往你班级里写答案。

## 4. Windows 版和 Linux 版的两点行为差异

- 本地样例评测的**内存限制在 Windows 上是生效的**（`OJProblemCasesRunner` 只在 Windows 采集/限制内存），Linux 下判不出 MLE。
- 判定严格度取决于 **选项 → 程序运行 → 题单**（默认「严格匹配」）。建议调成「忽略行首行尾空格」，更接近 OJ 的判法；插件那套「剥行尾空格+丢末尾空行」在这里靠建题时归一样例文件来近似实现。

其余已知限制（样例串行跑、不并发；站点无官方接口、改版可能失效、本地功能不联网照常可用）与 Linux 版一致。

## 5. 如果不想自己编译

fork 的 Actions 处于 GitHub 对 fork 的默认关闭状态（API 查到工作流数量 0，上游是 5）。你在浏览器打开 https://github.com/wylovecat/RedPanda-CPP/actions 点一次「I understand my workflows, go ahead and enable them」，之后我在云端跑同一个 `windows_legacy` job，把 `dist` 里的安装包下载下来直接放进这个坚果云文件夹，你这边一行命令都不用敲。
