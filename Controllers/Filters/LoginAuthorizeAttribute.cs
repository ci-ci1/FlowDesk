using FlowDesk.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace FlowDesk.Controllers.Filters
{
    public class LoginAuthorizeAttribute : AuthorizeAttribute
    {
        public override void OnAuthorization(AuthorizationContext filterContext)
        {
            if (filterContext == null || filterContext.HttpContext == null)
            {
                return;
            }

            var sessionInfo = filterContext.HttpContext.Session["LoginUser"];

            if (sessionInfo != null)
            {
                return;
            }

            // 未登录：重定向到登录页面
            filterContext.Result = new RedirectResult("/Login/LoginIndex");
        }
    

    }
}