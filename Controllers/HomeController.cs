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
            var userinfo = Session["LoginUser"] as Users;
            ViewBag.LoginUser = userinfo.RealName;

            // 1=目录 2=菜单（3按钮不渲染在左侧）
            var menus = db.Menus
                .Where(m => m.IsDeleted == false
                            && m.Status == 1
                            && m.IsVisible == true
                            && (m.MenuType == (byte)1 || m.MenuType == (byte)2))
                .OrderBy(m => m.Sort)
                .ThenBy(m => m.Id)
                .ToList();

            return View(menus);
        }
        #endregion

        //#region 菜单渲染
        //private List<MenuNode> GetMenuTree()
        //{
        //    // 只拿目录+菜单（按钮权限点先不渲染）
        //    var menuList = db.Menus
        //        .Where(m => m.IsDeleted == false
        //                    && m.Status == 1
        //                    && m.IsVisible == true
        //                    && (m.MenuType == 1 || m.MenuType == 2))
        //        .OrderBy(m => m.Sort)
        //        .ThenBy(m => m.Id)
        //        .ToList();

        //    var nodes = menuList.Select(m => new MenuNode
        //    {
        //        Id = m.Id,
        //        ParentId = m.ParentId,
        //        Name = m.Name,
        //        MenuType = m.MenuType,
        //        Url = m.Url,
        //        Icon = m.Icon,
        //        Sort = m.Sort
        //    }).ToList();

        //    return BuildMenuTree(nodes);
        //}

        //private List<MenuNode> BuildMenuTree(List<MenuNode> nodes)
        //{
        //    var dict = nodes.ToDictionary(x => x.Id, x => x);
        //    var roots = new List<MenuNode>();

        //    foreach (var n in nodes.OrderBy(x => x.Sort).ThenBy(x => x.Id))
        //    {
        //        if (n.ParentId == 0)
        //        {
        //            roots.Add(n);
        //        }
        //        else if (dict.ContainsKey(n.ParentId))
        //        {
        //            dict[n.ParentId].Children.Add(n);
        //        }
        //        else
        //        {
        //            // 父节点缺失时，兜底当根节点，避免菜单丢
        //            roots.Add(n);
        //        }
        //    }

        //    return roots;
        //}
        //#endregion

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

        public ActionResult a()
        {
            return View();
        }

    }
}