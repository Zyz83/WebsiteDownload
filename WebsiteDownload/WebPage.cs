using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebsiteDownload
{
    public class WebPage
    {
        string url;
        string html;
        string responseUrl;
        bool error;
        string errorMessage;
        bool serverSuccessReponse;
        byte[] fileData;

        public WebPage(string Url, string Html, string ResponseUrl)
        {
            url = Url;
            html = Html;
            responseUrl = ResponseUrl;
            error = false;
            errorMessage = "";
            serverSuccessReponse = true;
            fileData = null;
        }

        public WebPage(string Url, bool Error, string ErrorMessage, bool ServerSuccessReponse)
        {
            url = Url;
            html = "";
            responseUrl = "";
            error = Error;
            errorMessage = ErrorMessage;
            serverSuccessReponse = ServerSuccessReponse;
        }

        public WebPage(string Url, byte[] FileData, string ResponseUrl)
        {
            url = Url;
            fileData = FileData;
            responseUrl = ResponseUrl;
            error = false;
            errorMessage = "";
            serverSuccessReponse = true;
        }

        public byte[] FileData
        {
            get { return fileData; }
        }

        public string Url
        {
            get { return url; }
        }

        public string Html
        {
            get { return html; }
        }

        public string ResponseUrl
        {
            get { return responseUrl; }
        }

        public string ErrorMessage
        {
            get { return errorMessage; }
        }

        public bool Error
        {
            get { return error; }
        }

        public bool ServerSuccessReponse
        {
            get { return serverSuccessReponse; }
        }

        public override string ToString()
        {
            return Url + "\n\t" + Html;
        }
    }
}
