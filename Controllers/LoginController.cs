using FlowDesk.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using FlowDesk.Services;

namespace FlowDesk.Controllers
{
    public class LoginController : Controller
    {
        // GET: Login
        #region 公共基层方法
        /// <summary>
        /// 数据库连接
        /// </summary>
        FlowDeskEntities db = new FlowDeskEntities();
        /// <summary>
        /// 返回参数
        /// </summary>
        ReturnJsonData returnJsonData = new ReturnJsonData();
        #endregion

        #region 登录视图
        public ActionResult LoginIndex()
        {
            return View();
        }
        #endregion
        
        #region 注册视图
        public ActionResult Register()
        {
            return View();
        }
        #endregion

        #region 登录验证
        [HttpPost]
        public ActionResult Login(Users model)
        {
            try
            {
                var user = db.Users.FirstOrDefault(u => u.UserName == model.UserName && u.Password == model.Password);

                if (user == null)
                {
                    returnJsonData.code = 1;
                    returnJsonData.msg = "账号或密码错误";
                    return Json(returnJsonData);
                }

                Session["LoginUser"] = user;

                returnJsonData.code = 0;
                returnJsonData.msg = "登录成功";
                returnJsonData.data = new
                {
                    user.Id,
                    user.UserName
                };

                return Json(returnJsonData);
            }
            catch (Exception ex)
            {

                returnJsonData.code = -1;
                returnJsonData.msg = "系统异常，请联系管理员";
                returnJsonData.data = ex.Message;

                return Json(returnJsonData);
            }
        }
        #endregion

        #region 注册事件
        [HttpPost]
        public ActionResult Save(Users users)
        {
            try
            {
                if (users != null)
                {
                    var isExists = db.Users.FirstOrDefault(a => a.UserName == users.UserName);
                    if (isExists == null)
                    {
                        users.RealName = "员工";
                        users.Status = 1;
                        users.CreatedAt = DateTime.Now;
                        users.UpdatedAt = DateTime.Now;
                        db.Users.Add(users);
                        if (db.SaveChanges() > 0)
                        {
                            returnJsonData.code = 0;
                            returnJsonData.msg = "注册成功，请登录";
                        }
                        else
                        {
                            returnJsonData.code = 1;
                            returnJsonData.msg = "注册失败！";
                        }
                    }
                    else
                    {
                        returnJsonData.code = 1;
                        returnJsonData.msg = "注册失败，该用户已存在";
                    }
                }
                else
                {
                    returnJsonData.code = 1;
                    returnJsonData.msg = "注册失败";

                }
            }
            catch (Exception ex)
            {
                returnJsonData.code = 1;
                returnJsonData.msg = "注册失败" + ex.Message;
            }
            return Json(returnJsonData);
        }
        #endregion


    }
}