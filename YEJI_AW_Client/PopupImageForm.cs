using System;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    public partial class PopupImageForm : Form
    {
        public PopupImageForm(string imageUrl)
        {
            InitializeComponent();

            this.StartPosition = FormStartPosition.CenterScreen;
            this.Width = 760;
            this.Height = 620;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            try
            {
                pictureBox1.Load(imageUrl);
            }
            catch
            {
                pictureBox1.Image = null;
            }

            // 이미지 더블클릭하면 팝업 닫힘
            pictureBox1.DoubleClick += (s, e) => this.Close();

            this.FormClosed += (s, e) =>
            {
                if (pictureBox1.Image != null)
                {
                    pictureBox1.Image.Dispose();
                    pictureBox1.Image = null;
                }

                pictureBox1.Dispose();
                MemoryOptimizer.TrimWorkingSet();
            };
        }
    }
}
