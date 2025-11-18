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
            this.Width = 1048;
            this.Height = 814;
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
        }
    }
}
