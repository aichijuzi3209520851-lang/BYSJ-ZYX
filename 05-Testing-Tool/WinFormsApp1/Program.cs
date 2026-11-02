using System.Text;

namespace IotLinkLab
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Diag.InstallMessageCounter();
            Application.Run(new Form1());
        }
    }
}
