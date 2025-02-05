// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class WoodgroveCredential {
    public string firstName { get; set; }
    public string lastName { get; set; }
    [JsonProperty( NullValueHandling = NullValueHandling.Ignore )]
    public string address { get; set; }
    [JsonProperty( NullValueHandling = NullValueHandling.Ignore )]
    public string photo { get; set; }
    [JsonProperty( NullValueHandling = NullValueHandling.Ignore )]
    public string company { get; set; }
    [JsonProperty( NullValueHandling = NullValueHandling.Ignore )]
    public string department { get; set; }
    [JsonProperty( NullValueHandling = NullValueHandling.Ignore )]
    public string title { get; set; }
    [JsonProperty( NullValueHandling = NullValueHandling.Ignore )]
    public string documentNumber { get; set; }
}

