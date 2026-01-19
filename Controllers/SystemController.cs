using FlowDesk.Controllers.Filters;
using FlowDesk.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace FlowDesk.Controllers
{
    [LoginAuthorize]
    public class SystemController : Controller
    {
        private readonly FlowDeskEntities db = new FlowDeskEntities();

        // ========== 用户管理页面 ==========
        public ActionResult User()
        {
            return View();
        }

        // 用户列表数据（Layui table）
        [HttpGet]
        public ActionResult UserListJson(int page = 1, int limit = 10, string keyword = null, byte? status = null)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" }, JsonRequestBehavior.AllowGet);

            // 简化：只有管理员可以管理用户
            if (!IsAdmin(me.Id)) return Json(new { code = 1, msg = "无权限（仅管理员）" }, JsonRequestBehavior.AllowGet);

            var q = db.Users.AsQueryable();

            if (!string.IsNullOrWhiteSpace(keyword))
                q = q.Where(u => u.UserName.Contains(keyword) || u.RealName.Contains(keyword));

            if (status.HasValue)
                q = q.Where(u => u.Status == status.Value);

            var total = q.Count();

            var list = q.OrderByDescending(u => u.Id)
                .Skip((page - 1) * limit)
                .Take(limit)
                .Select(u => new
                {
                    u.Id,
                    u.UserName,
                    u.RealName,
                    u.Status
                })
                .ToList();

            return Json(new { code = 0, msg = "", count = total, data = list }, JsonRequestBehavior.AllowGet);
        }

        // 启用/禁用用户
        [HttpPost]
        public ActionResult UserSetStatus(long id, byte status)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });
            if (!IsAdmin(me.Id)) return Json(new { code = 1, msg = "无权限（仅管理员）" });

            var u = db.Users.FirstOrDefault(x => x.Id == id);
            if (u == null) return Json(new { code = 1, msg = "用户不存在" });

            u.Status = status;
            db.SaveChanges();

            return Json(new { code = 0, msg = "ok" });
        }

        // 分配角色弹窗页
        public ActionResult UserAssignRole(long id)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Content("未登录");
            if (!IsAdmin(me.Id)) return Content("无权限（仅管理员）");

            var user = db.Users.FirstOrDefault(x => x.Id == id);
            if (user == null) return Content("用户不存在");

            var roles = db.Roles.Where(r => r.IsDeleted == false && r.Status == 1)
                .OrderBy(r => r.Id).ToList();

            var selected = db.UserRoles.Where(ur => ur.UserId == id).Select(ur => ur.RoleId).ToList();

            ViewBag.User = user;
            ViewBag.Roles = roles;
            ViewBag.Selected = selected;

            return View();
        }

        // 保存用户角色（覆盖式）
        [HttpPost]
        public ActionResult UserAssignRolePost(long userId, string roleIds)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });
            if (!IsAdmin(me.Id)) return Json(new { code = 1, msg = "无权限（仅管理员）" });

            var ids = (roleIds ?? "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Convert.ToInt64(s))
                .Distinct()
                .ToList();

            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    var old = db.UserRoles.Where(x => x.UserId == userId).ToList();
                    db.UserRoles.RemoveRange(old);
                    db.SaveChanges();

                    foreach (var rid in ids)
                    {
                        db.UserRoles.Add(new UserRoles
                        {
                            UserId = userId,
                            RoleId = rid,
                            CreatedAt = DateTime.Now
                        });
                    }
                    db.SaveChanges();

                    tx.Commit();
                    return Json(new { code = 0, msg = "ok" });
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return Json(new { code = 1, msg = "保存失败：" + ex.Message });
                }
            }
        }

        // ===== 工具：判断 admin =====
        private bool IsAdmin(long userId)
        {
            return (from ur in db.UserRoles
                    join r in db.Roles on ur.RoleId equals r.Id
                    where ur.UserId == userId
                          && r.IsDeleted == false
                          && r.Status == 1
                          && r.Code == "admin"
                    select r.Id).Any();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}