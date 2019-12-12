﻿using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SecretNest.ShortUrl
{
    public static class ContextProcessFacade
    {
        public static async Task Process(HttpContext context, Func<Task> nextHandler)
        {
            var host = context.GetHost();
            var accessKey = context.GetAccessKey();

            if (SettingHost.ServiceSetting.GlobalManagementKey == accessKey &&
                (SettingHost.ServiceSetting.GlobalManagementEnabledHosts.Count == 0 || SettingHost.ServiceSetting.GlobalManagementEnabledHosts.Contains(host)))
            {
                //Global Management
                try
                {
                    var result = GlobalManager.GlobalManage(context);
                    await context.ProcessOtherResultAsync(result);
                }
                catch
                {
                    await context.ProcessOtherResultAsync(new Status500Result());
                }
            }
            else
            {
                //Alias remap
                while (SettingHost.ServiceSetting.Aliases.TryGetValue(host, out string target))
                {
                    host = target;
                }

                if (SettingHost.ServiceSetting.Domains.TryGetValue(host, out DomainSetting domain))
                {
                    //Domain matched
                    if (accessKey == domain.ManagementKey)
                    {
                        //Domain Management
                        try
                        {
                            var result = DomainManager.DomainManage(context, domain);
                            await context.ProcessOtherResultAsync(result);
                        }
                        catch
                        {
                            await context.ProcessOtherResultAsync(new Status500Result());
                        }
                    }
                    else if (domain.Redirects.TryGetValue(accessKey, out RedirectTarget target))
                    {
                        //Record matched
                        context.Redirect(target);
                    }
                    else
                    {
                        //Domain default
                        context.Redirect(domain.DefaultTarget);
                    }
                }
                else
                {
                    //Global default
                    context.Redirect(SettingHost.ServiceSetting.DefaultTarget);
                }
            }
        }

        internal static string GetHost(this HttpContext context)
        {
            if (SettingHost.ServiceSetting.PreferXForwardedHost)
            {
                var xForwardedHost = context.Request.Headers["X-Forwarded-Host"].FirstOrDefault();
                if (!string.IsNullOrEmpty(xForwardedHost))
                {
                    return xForwardedHost;
                }
            }
            if (context.Request.Host.Port != null && context.Request.Host.Port != 80 && context.Request.Host.Port != 443)
            {
                return string.Format("{0}:{1}", context.Request.Host.Host, context.Request.Host.Port);
            }
            else
            {
                return context.Request.Host.Value;
            }
        }

        internal static string GetQueryTextParameter(this HttpContext context, string parameter)
        {
            return context.Request.GetQueryTextParameter(parameter);
        }

        internal static string GetQueryOptionalTextParameter(this HttpContext context, string parameter)
        {
            return context.Request.GetQueryOptionalTextParameter(parameter);
        }

        internal static bool GetQueryBooleanParameter(this HttpContext context, string parameter)
        {
            return context.Request.GetQueryBooleanParameter(parameter);
        }

        internal static string GetQueryTextParameter(this HttpRequest request, string parameter)
        {
            return request.Query[parameter].First();
        }

        internal static string GetQueryOptionalTextParameter(this HttpRequest request, string parameter)
        {
            return request.Query[parameter].FirstOrDefault();
        }

        internal static bool GetQueryBooleanParameter(this HttpRequest request, string parameter)
        {
            return request.Query[parameter].FirstOrDefault() == "1";
        }

        static string GetAccessKey(this HttpContext context)
        {
            if (context.Request.Path.HasValue)
            {
                return context.Request.Path.Value.Substring(1);
            }
            else
            {
                return null;
            }
        }

        static void Redirect(this HttpContext context, RedirectTarget target)
        {
            var url = target.Target;

            var query = context.Request.QueryString;
            if (query.HasValue && query.Value != string.Empty)
            {
                if (target.QueryProcess == RedirectQueryProcess.AppendDirectly)
                {
                    url += query.Value;
                }
                else if (target.QueryProcess == RedirectQueryProcess.AppendRemovingLeadingQuestionMark)
                {
                    if (query.Value.StartsWith("?"))
                        url += "&" + query.Value.Substring(1);
                    else
                        url += "&" + query.Value;
                }
            }

            context.Response.Redirect(target.Target, target.Permanent);
        }

        static async Task ProcessOtherResultAsync(this HttpContext context, OtherResult result)
        {
            context.Response.StatusCode = result.StatusCode;
            if (result.HasContent)
            {
                context.Response.ContentType = result.ContentType;
                await context.Response.WriteAsync(result.Context);
            }
        }
    }
}
