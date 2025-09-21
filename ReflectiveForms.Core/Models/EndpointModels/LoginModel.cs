// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using Newtonsoft.Json;

namespace ReflectiveForms.Core.Models.EndpointModels;

public class LoginInputModel
{
    [JsonProperty("email")]
    public string EmailAddress = "";

    [JsonProperty("password")]
    public string Password = "";

    [JsonProperty("captcha_answer")]
    public int? CaptchaAnswer { get; set; }
}

public class LoginOutputModel
{
    [JsonProperty("token")]
    public string Token = "";
}
