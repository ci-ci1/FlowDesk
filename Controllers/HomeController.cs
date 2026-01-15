using FlowDesk.Controllers.Filters;
using FlowDesk.Models;
using FlowDesk.Models.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace FlowDesk.Controllers
{
    [LoginAuthorize]
    public class HomeController : Controller
    {

        #region 公共基层方法
        /// <summary>
        /// 数据库连接
        /// </summary>
        FlowDeskEntities db = new FlowDeskEntities();
        /// <summary>
        /// 返回参数
        /// </summary>
        ReturnJsonData returnJsonData = new ReturnJsonData();
        /// <summary>
        /// 当前登录用户信息
        /// </summary>
        /// <returns></returns>
        public Users userinfo
        {
            get
            {
                return Session["LoginUser"] as Users;
            }
            set
            {
                Session["LoginUser"] = value;
            }
        }
        #endregion

        // GET: Home
        #region 欢迎页
        public ActionResult Welcome()
        {
            return View();
        }
        #endregion

        #region 首页视图
        public ActionResult Index()
        {
            ViewBag.LoginUser = userinfo.RealName;

            var menus = (from m in db.Menus
                         join rm in db.RoleMenus on m.Id equals rm.MenuId
                         join ur in db.UserRoles on rm.RoleId equals ur.RoleId
                         where ur.UserId == userinfo.Id
                               && m.IsDeleted == false
                               && m.Status == 1
                               && m.IsVisible == true
                               && (m.MenuType == (byte)1 || m.MenuType == (byte)2)
                         select m)
                .Distinct()
                .OrderBy(m => m.Sort)
                .ThenBy(m => m.Id)
                .ToList();

            return View(menus);
        }
        #endregion

        #region 个人信息视图
        public ActionResult ViewPersonalInfo()
        {
            if (userinfo != null)
            {
                ViewBag.Id = userinfo.Id;
                ViewBag.UserName = userinfo.UserName;
                ViewBag.RealName = userinfo.RealName;
                ViewBag.Email = userinfo.Email;
                ViewBag.Phone = userinfo.Phone;
                ViewBag.Status = userinfo.Status;
                ViewBag.CreatedAt = userinfo.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                ViewBag.UpdatedAt = userinfo.UpdatedAt.ToString("yyyy-MM-dd HH:mm");
            }

            return View();
        }
        #endregion

        #region 修改密码视图
        public ActionResult ChangePasswordView()
        {
            ViewBag.Id = userinfo.Id;
            return View();
        }
        #endregion

        #region 修改个人信息
        public ActionResult SavePersonalInfo(Users users)
        {
            var user = db.Users.Find(users.Id);
            if (user != null) 
            {
                user.Email = users.Email;
                user.Phone = users.Phone;
                user.RealName = users.RealName;
                db.Entry(user).State = System.Data.Entity.EntityState.Modified;
                if (db.SaveChanges() > 0)
                {
                    returnJsonData.code = 0;
                    returnJsonData.msg = "修改成功！";
                }
                else
                {
                    returnJsonData.code = 1;
                    returnJsonData.msg = "修改失败！";
                }
            }
            else
            {
                returnJsonData.code = 1;
                returnJsonData.msg = "当前用户不存在，请退出重新登录！";
            }


            return Json(returnJsonData);
        }
        #endregion

        #region 修改密码
        public ActionResult ChangePassword(int Id, string OldPassword, string NewPassword)
        {
            try
            {
                var user = db.Users.FirstOrDefault(u => u.Id == Id);
                if (user == null)
                {
                    returnJsonData.code = 1;
                    returnJsonData.msg = "用户不存在，请退出重新登录！";
                }
                else if(OldPassword==userinfo.Password)
                {
                    user.Password = NewPassword;
                    db.Entry(user).State = System.Data.Entity.EntityState.Modified;
                    if (db.SaveChanges() > 0)
                    {
                        returnJsonData.code = 0;
                        returnJsonData.msg = "修改成功！";
                    }
                    else
                    {
                        returnJsonData.code = 1;
                        returnJsonData.msg = "修改失败！";
                    }
                }
                else
                {
                    returnJsonData.code = 1;
                    returnJsonData.msg = "原密码有误，请重新输入！";
                }
            }
            catch (Exception ex)
            {
                returnJsonData.code = 1;
                returnJsonData.msg = "系统异常，请联系管理员！";
            }
            return Json(returnJsonData);
        }
        #endregion

        #region 退出登录
        public ActionResult SignOut()
        {
            try
            {
                Session["LoginUser"] = null;
                returnJsonData.code = 0;
                return Json(returnJsonData);
            }
            catch (Exception ex)
            {
                returnJsonData.code = 1;
                returnJsonData.msg = "退出失败" + ex.Message;
                return Json(returnJsonData);
            }
        }
        #endregion
    }
}