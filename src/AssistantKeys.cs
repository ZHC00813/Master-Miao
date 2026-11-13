using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SWBodyOrganizer
{
    internal sealed class AssistantKeys
    {
        public Dictionary<string,string> Entries { get; set; } = new Dictionary<string,string>();
        public string Active { get; set; }
        private static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MasterMiao");
        private static readonly string FilePath = Path.Combine(DirectoryPath,"assistant-keys.dpapi");
        private static string Read(string path) => Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser));
        public static AssistantKeys Load()
        {
            if(File.Exists(FilePath)) return new JavaScriptSerializer().Deserialize<AssistantKeys>(Read(FilePath));
            var keys=new AssistantKeys();
            string legacy=Path.Combine(DirectoryPath,"deepseek.dpapi");
            if(File.Exists(legacy)) { keys.Active="DeepSeek"; keys.Entries.Add(keys.Active,Read(legacy)); }
            return keys;
        }
        public static string Current()
        {
            var keys=Load();
            if(keys.Active!=null && keys.Entries.TryGetValue(keys.Active,out var value)) return value;
            string fallback=Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
            if(!string.IsNullOrWhiteSpace(fallback)) return fallback;
            throw new InvalidOperationException("请在左侧设置中添加并选择 DeepSeek API Key。");
        }
        public void Save()
        {
            Directory.CreateDirectory(DirectoryPath);
            string tmp=FilePath+"."+Guid.NewGuid().ToString("N");
            try
            {
                var data=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(this));
                File.WriteAllBytes(tmp,ProtectedData.Protect(data,null,DataProtectionScope.CurrentUser));
                if(File.Exists(FilePath)) File.Replace(tmp,FilePath,null); else File.Move(tmp,FilePath);
            }
            finally { if(File.Exists(tmp)) File.Delete(tmp); }
        }
    }

    internal sealed class KeyEditor : FlowLayoutPanel
    {
        private readonly AssistantKeys keys=AssistantKeys.Load();
        private readonly ComboBox names=new ComboBox { Width=155 };
        private readonly TextBox secret=new TextBox { Width=190, UseSystemPasswordChar=true, AccessibleName="API Key" };
        private bool changed;
        public KeyEditor()
        {
            names.AccessibleName="Key 名称";
            var set=new Button {Text="添加 / 更新",AutoSize=true};
            var remove=new Button {Text="删除",AutoSize=true};
            Controls.AddRange(new Control[]{names,secret,set,remove});
            RefreshNames();
            names.SelectionChangeCommitted+=(s,e)=> { keys.Active=names.SelectedItem.ToString(); secret.Clear(); changed=true; };
            set.Click+=(s,e)=> { try { Stage(); } catch(ArgumentException ex) { MessageBox.Show(this,ex.Message); } };
            remove.Click+=(s,e)=> { if(keys.Entries.Remove(names.Text.Trim())) { if(!keys.Entries.ContainsKey(keys.Active ?? ""))keys.Active=keys.Entries.Keys.FirstOrDefault(); changed=true;RefreshNames(); } };
        }
        private void RefreshNames()
        {
            names.Items.Clear();names.Items.AddRange(keys.Entries.Keys.ToArray());names.Text=keys.Active ?? "";secret.Clear();
        }
        private void Stage()
        {
            string name=names.Text.Trim(),value=secret.Text.Trim();
            if(name.Length==0 || value.Length==0) throw new ArgumentException("请输入条目名称和 API Key。");
            keys.Entries[name]=value;keys.Active=name;changed=true;RefreshNames();
        }
        public void Commit()
        {
            if(secret.Text.Length>0) Stage();
            if(changed) keys.Save();
        }
    }
}
