using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TrueIdentityIncV2.Helpers
{
    public static class JsonUtils
    {
        public static JObject FromJsonString( string json ) {
            return JObject.Parse( json );
        }
        public static string ToJsonString( object value ) {
            return JsonConvert.SerializeObject( value, Newtonsoft.Json.Formatting.None, new JsonSerializerSettings {
                NullValueHandling = NullValueHandling.Ignore
            } );
        }
    }
}
