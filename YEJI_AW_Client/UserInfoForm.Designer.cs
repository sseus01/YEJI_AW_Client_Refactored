namespace YEJI_AW_Client
{
    partial class UserInfoForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.TextBox textBoxName;
        private System.Windows.Forms.TextBox textBoxId;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Button buttonSave;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(UserInfoForm));
            textBoxName = new TextBox();
            textBoxId = new TextBox();
            label1 = new Label();
            label2 = new Label();
            buttonSave = new Button();
            SuspendLayout();
            // 
            // textBoxName
            // 
            textBoxName.Location = new Point(80, 24);
            textBoxName.Name = "textBoxName";
            textBoxName.Size = new Size(155, 23);
            textBoxName.TabIndex = 1;
            // 
            // textBoxId
            // 
            textBoxId.Location = new Point(80, 66);
            textBoxId.Name = "textBoxId";
            textBoxId.MaxLength = 6;
            textBoxId.Size = new Size(155, 23);
            textBoxId.TabIndex = 3;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(22, 27);
            label1.Name = "label1";
            label1.Size = new Size(34, 15);
            label1.TabIndex = 0;
            label1.Text = "이름:";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(22, 69);
            label2.Name = "label2";
            label2.Size = new Size(34, 15);
            label2.TabIndex = 2;
            label2.Text = "사번:";
            // 
            // buttonSave
            // 
            buttonSave.Location = new Point(102, 107);
            buttonSave.Name = "buttonSave";
            buttonSave.Size = new Size(75, 27);
            buttonSave.TabIndex = 4;
            buttonSave.Text = "저장";
            buttonSave.UseVisualStyleBackColor = true;
            buttonSave.Click += buttonSave_Click;
            // 
            // UserInfoForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(264, 151);
            Controls.Add(buttonSave);
            Controls.Add(textBoxId);
            Controls.Add(label2);
            Controls.Add(textBoxName);
            Controls.Add(label1);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "UserInfoForm";
            Text = "사용자 정보 입력";
            ResumeLayout(false);
            PerformLayout();
        }
    }
}
