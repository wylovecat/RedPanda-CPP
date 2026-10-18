namespace RedPanda.Competition.Services;

public static class TemplateService
{
    public const string DefaultFileName = "未命名.cpp";

    public static string CreateCppTemplate()
    {
        return """
               #include <bits/stdc++.h>
               using namespace std;

               int main() {
                   ios::sync_with_stdio(false);
                   cin.tie(nullptr);

                   // 在这里写代码

                   return 0;
               }
               """;
    }
}
