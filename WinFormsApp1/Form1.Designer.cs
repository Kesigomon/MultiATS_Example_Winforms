namespace WinFormsApp1;

partial class Form1
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }

        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        LoginButton = new System.Windows.Forms.Button();
        SuspendLayout();
        // 
        // LoginButton
        // 
        LoginButton.Location = new System.Drawing.Point(205, 70);
        LoginButton.Name = "LoginButton";
        LoginButton.Size = new System.Drawing.Size(385, 304);
        LoginButton.TabIndex = 0;
        LoginButton.Text = "ログインボタン";
        LoginButton.UseVisualStyleBackColor = true;
        LoginButton.Click += LoginButton_Click;
        // 
        // Form1
        // 
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        ClientSize = new System.Drawing.Size(800, 450);
        Controls.Add(LoginButton);
        Text = "Form1";
        ResumeLayout(false);
    }

    private System.Windows.Forms.Button LoginButton;

    #endregion
}