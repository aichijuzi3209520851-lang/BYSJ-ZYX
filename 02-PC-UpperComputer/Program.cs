using System;
using System.Windows.Forms;

namespace DHT11
{
    internal static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// 流程: 登录窗 → 主界面 → (主界面点"注销"则回到登录窗, 否则退出)。
        /// 用 while 循环而不是嵌套回调, 是为了让"注销后能重新登录"这件事只有一处控制点。
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            while (true)
            {
                using (LoginForm loginForm = new LoginForm())
                {
                    if (loginForm.ShowDialog() != DialogResult.OK)
                    {
                        return;  // 用户取消登录, 直接退出程序
                    }
                }

                bool logoutRequested;
                using (Form1 mainForm = new Form1())
                {
                    Application.Run(mainForm);
                    logoutRequested = mainForm.LogoutRequested;
                }

                if (!logoutRequested)
                {
                    return;  // 正常关闭窗口, 退出程序
                }
                // 否则继续下一轮: 重新弹出登录窗
            }
        }
    }
}
