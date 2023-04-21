using System;
using System.Collections.Generic;
using System.Net.Http.Formatting;
using System.Text;

namespace SMDesktopUI.Library.Helpers
{
    public class VndApiJsonMediaTypeFormatter : JsonMediaTypeFormatter
    {
        public VndApiJsonMediaTypeFormatter()
        {
            SupportedMediaTypes.Add(new System.Net.Http.Headers.MediaTypeHeaderValue("text/html"));
        }
    }
}
