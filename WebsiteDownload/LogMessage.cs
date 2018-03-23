using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebsiteDownload
{
    class LogMessage
    {
        public string Filepath { get; set; }
        public string Text { get; set; }

        public LogMessage(string FilePath, string MessageText)
        {
            Filepath = FilePath;
            Text = MessageText;
        }
    }
}
