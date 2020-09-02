using Newtonsoft.Json;
using System;
using System.ComponentModel;

namespace SimplygonFunctionApp.Models
{
    internal class PostData
    {
        public Uri InputZipUri { get; set; }

        [DefaultValue(AzRemeshFn.DEFAULT_SCREEN_SIZE)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public uint OnScreenSize { get; set; }
    }
}
