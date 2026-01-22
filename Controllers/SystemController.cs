using FlowDesk.Controllers.Filters;
using FlowDesk.Models;
using FlowDesk.Models.ViewModels;
using FlowDesk.Services;
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
        private string J(object obj) => new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(obj);

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

            var list = q.OrderBy(u => u.Id)
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
            var beforeJson = J(new
            {
                u.Id,
                u.UserName,
                Status = u.Status
            });
            u.Status = status;
            AuditService.Add(db, "User", u.Id, "SET_STATUS", me,
    "用户状态变更：" + u.UserName + " -> " + status);
            var afterJson = J(new
            {
                u.Id,
                u.UserName,
                Status = u.Status
            });

            AuditService.Add(db, "User", u.Id, "SET_STATUS", me,
                "用户状态变更：" + u.UserName + " -> " + status,
                beforeJson: beforeJson,
                afterJson: afterJson);
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
                    AuditService.Add(db, "User", userId, "ASSIGN_ROLE", me,
    "分配用户角色：UserId=" + userId + "；RoleIds=" + (roleIds ?? ""));
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




        // ========== 角色管理页面 ==========
        public ActionResult Role()
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return RedirectToAction("LoginIndex", "Login");
            if (!IsAdmin(me.Id)) return Content("无权限（仅管理员）");

            return View();
        }

        // 角色列表
        [HttpGet]
        public ActionResult RoleListJson(int page = 1, int limit = 10, string keyword = null, byte? status = null)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" }, JsonRequestBehavior.AllowGet);
            if (!IsAdmin(me.Id)) return Json(new { code = 1, msg = "无权限（仅管理员）" }, JsonRequestBehavior.AllowGet);

            var q = db.Roles.Where(r => r.IsDeleted == false);

            if (!string.IsNullOrWhiteSpace(keyword))
                q = q.Where(r => r.Name.Contains(keyword) || r.Code.Contains(keyword));

            if (status.HasValue)
                q = q.Where(r => r.Status == status.Value);

            var total = q.Count();

            var list = q.OrderBy(r => r.Id)
                .Skip((page - 1) * limit)
                .Take(limit)
                .Select(r => new
                {
                    r.Id,
                    r.Code,
                    r.Name,
                    r.Status,
                    r.Remark
                })
                .ToList();

            return Json(new { code = 0, msg = "", count = total, data = list }, JsonRequestBehavior.AllowGet);
        }

        // 新增角色页
        public ActionResult RoleCreate()
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Content("未登录");
            if (!IsAdmin(me.Id)) return Content("无权限（仅管理员）");
            return View();
        }

        [HttpPost]
        public ActionResult RoleCreatePost(string code, string name, string remark)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });
            if (!IsAdmin(me.Id)) return Json(new { code = 1, msg = "无权限（仅管理员）" });

            if (string.IsNullOrWhiteSpace(code)) return Json(new { code = 1, msg = "Code 不能为空" });
            if (string.IsNullOrWhiteSpace(name)) return Json(new { code = 1, msg = "Name 不能为空" });

            code = code.Trim();

            if (db.Roles.Any(r => r.IsDeleted == false && r.Code == code))
                return Json(new { code = 1, msg = "角色编码已存在" });

            db.Roles.Add(new FlowDesk.Models.Roles
            {
                Code = code,
                Name = name.Trim(),
                Remark = remark,
                Status = 1,
                IsDeleted = false,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });

            db.SaveChanges();
            return Json(new { code = 0, msg = "ok" });
        }

        // 编辑角色页
        public ActionResult RoleEdit(long id)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Content("未登录");
            if (!IsAdmin(me.Id)) return Content("无权限（仅管理员）");

            var role = db.Roles.FirstOrDefault(r => r.Id == id && r.IsDeleted == false);
            if (role == null) return Content("角色不存在");

            return View(role);
        }

        [HttpPost]
        public ActionResult RoleEditPost(long id, string name, string remark)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });
            if (!IsAdmin(me.Id)) return Json(new { code = 1, msg = "无权限（仅管理员）" });

            var role = db.Roles.FirstOrDefault(r => r.Id == id && r.IsDeleted == false);
            if (role == null) return Json(new { code = 1, msg = "角色不存在" });

            if (string.IsNullOrWhiteSpace(name)) return Json(new { code = 1, msg = "Name 不能为空" });

            role.Name = name.Trim();
            role.Remark = remark;
            role.UpdatedAt = DateTime.Now;

            db.SaveChanges();
            return Json(new { code = 0, msg = "ok" });
        }

        // 启用/禁用角色
        [HttpPost]
        public ActionResult RoleSetStatus(long id, byte status)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });
            if (!IsAdmin(me.Id)) return Json(new { code = 1, msg = "无权限（仅管理员）" });

            var role = db.Roles.FirstOrDefault(r => r.Id == id && r.IsDeleted == false);
            if (role == null) return Json(new { code = 1, msg = "角色不存在" });

            // 可选：不允许禁用 admin 角色
            if (role.Code == "admin" && status == 0)
                return Json(new { code = 1, msg = "不允许禁用 admin 角色" });

            role.Status = status;
            role.UpdatedAt = DateTime.Now;
            AuditService.Add(db, "Role", role.Id, "ROLE_SET_STATUS", me,
    "角色状态变更：" + role.Code + " -> " + status);
            db.SaveChanges();

            return Json(new { code = 0, msg = "ok" });
        }





        // ========== 角色授权菜单页面 ==========
        public ActionResult RoleMenu()
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return RedirectToAction("LoginIndex", "Login");
            if (!IsAdmin(me.Id)) return Content("无权限（仅管理员）");

            ViewBag.Roles = db.Roles
                .Where(r => r.IsDeleted == false && r.Status == 1)
                .OrderBy(r => r.Id)
                .ToList();

            return View();
        }

        // 获取某角色的菜单勾选数据
        [HttpGet]
        public ActionResult RoleMenuData(long roleId)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" }, JsonRequestBehavior.AllowGet);
            if (!IsAdmin(me.Id)) return Json(new { code = 1, msg = "无权限（仅管理员）" }, JsonRequestBehavior.AllowGet);

            var menus = db.Menus
                .Where(m => m.IsDeleted == false && m.Status == 1 && m.IsVisible == true
                            && (m.MenuType == (byte)1 || m.MenuType == (byte)2))
                .OrderBy(m => m.Sort).ThenBy(m => m.Id)
                .Select(m => new { m.Id, m.ParentId, m.Name, m.MenuType })
                .ToList();

            var selectedIds = db.RoleMenus
                .Where(rm => rm.RoleId == roleId)
                .Select(rm => rm.MenuId)
                .ToList();

            return Json(new { code = 0, data = new { menus, selectedIds } }, JsonRequestBehavior.AllowGet);
        }

        // 保存角色菜单（覆盖式）
        [HttpPost]
        public ActionResult SaveRoleMenu(long roleId, string menuIds)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });
            if (!IsAdmin(me.Id)) return Json(new { code = 1, msg = "无权限（仅管理员）" });

            var ids = (menuIds ?? "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Convert.ToInt64(s))
                .Distinct()
                .ToList();

            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    // ===== beforeJson：旧授权菜单 =====
                    var oldIds = db.RoleMenus
                        .Where(x => x.RoleId == roleId)
                        .Select(x => x.MenuId)
                        .ToList();

                    var beforeJson = new System.Web.Script.Serialization.JavaScriptSerializer()
                        .Serialize(new { RoleId = roleId, MenuIds = oldIds });

                    // 删除旧授权
                    var old = db.RoleMenus.Where(x => x.RoleId == roleId).ToList();
                    db.RoleMenus.RemoveRange(old);
                    db.SaveChanges();

                    // 插入新授权
                    foreach (var id in ids)
                    {
                        db.RoleMenus.Add(new RoleMenus
                        {
                            RoleId = roleId,
                            MenuId = id,
                            CreatedAt = DateTime.Now
                        });
                    }

                    // ===== afterJson：新授权菜单 =====
                    var afterJson = new System.Web.Script.Serialization.JavaScriptSerializer()
                        .Serialize(new { RoleId = roleId, MenuIds = ids });

                    // 写审计（放在最后一次 SaveChanges 前）
                    AuditService.Add(
                        db,
                        "MenuAuth",
                        roleId,
                        "ROLE_MENU",
                        me,
                        "角色菜单授权：RoleId=" + roleId + "；MenuIds=" + (menuIds ?? ""),
                        beforeJson: beforeJson,
                        afterJson: afterJson
                    );

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


        // ===================== 审计日志 =====================

        // 审计日志页面
        public ActionResult Audit()
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return RedirectToAction("LoginIndex", "Login");
            if (!IsAdmin(me.Id)) return Content("无权限（仅管理员）");

            return View();
        }

        // 审计日志列表数据（Layui table）
        [HttpGet]
        public ActionResult AuditListJson(
            int page = 1,
            int limit = 20,
            string bizType = null,
            string act = null,
            string keyword = null,
            DateTime? from = null,
            DateTime? to = null)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" }, JsonRequestBehavior.AllowGet);
            if (!IsAdmin(me.Id)) return Json(new { code = 1, msg = "无权限（仅管理员）" }, JsonRequestBehavior.AllowGet);

            var q = db.AuditLogs.AsQueryable();

            if (!string.IsNullOrWhiteSpace(bizType))
                q = q.Where(x => x.BizType == bizType);

            if (!string.IsNullOrWhiteSpace(act))
                q = q.Where(x => x.Action.Contains(act));

            if (!string.IsNullOrWhiteSpace(keyword))
                q = q.Where(x => x.OperatorName.Contains(keyword) || x.Remark.Contains(keyword));

            if (from.HasValue)
                q = q.Where(x => x.CreatedAt >= from.Value);

            if (to.HasValue)
                q = q.Where(x => x.CreatedAt <= to.Value);

            var total = q.Count();

            var list = q.OrderByDescending(x => x.CreatedAt)
                .Skip((page - 1) * limit)
                .Take(limit)
                .Select(x => new
                {
                    x.Id,
                    x.BizType,
                    x.BizId,
                    x.Action,
                    x.OperatorName,
                    x.Remark,
                    x.CreatedAt
                })
                .ToList();
            return Json(new { code = 0, msg = "", count = total, data = list }, JsonRequestBehavior.AllowGet);

            
        }


        public ActionResult AuditDetail(long id)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Content("未登录");
            if (!IsAdmin(me.Id)) return Content("无权限（仅管理员）");

            var log = db.AuditLogs.FirstOrDefault(x => x.Id == id);
            if (log == null) return Content("记录不存在");

            return View(log);
        }

        // ===================== 菜单管理 =====================

        // 菜单管理页
        public ActionResult Menu()
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return RedirectToAction("LoginIndex", "Login");
            if (!IsAdmin(me.Id)) return Content("无权限（仅管理员）");
            return View();
        }

        // 菜单列表（两级：目录+菜单）
        [HttpGet]
        public ActionResult MenuListJson()
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" }, JsonRequestBehavior.AllowGet);
            if (!IsAdmin(me.Id)) return Json(new { code = 1, msg = "无权限（仅管理员）" }, JsonRequestBehavior.AllowGet);

            var list = db.Menus
                .Where(m => m.IsDeleted == false && (m.MenuType == (byte)1 || m.MenuType == (byte)2))
                .OrderBy(m => m.Sort).ThenBy(m => m.Id)
                .Select(m => new
                {
                    m.Id,
                    m.ParentId,
                    m.Name,
                    m.MenuType,
                    m.Url,
                    m.Icon,
                    m.Sort,
                    m.IsVisible,
                    m.Status
                })
                .ToList();

            return Json(new { code = 0, data = list }, JsonRequestBehavior.AllowGet);
        }

        // 新增菜单页
        public ActionResult MenuCreate()
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Content("未登录");
            if (!IsAdmin(me.Id)) return Content("无权限（仅管理员）");

            // 下拉：父目录（ParentId=0 & MenuType=1）
            ViewBag.Roots = db.Menus.Where(m => m.IsDeleted == false && m.ParentId == 0 && m.MenuType == (byte)1)
                .OrderBy(m => m.Sort).ThenBy(m => m.Id).ToList();

            return View();
        }

        [HttpPost]
        public ActionResult MenuCreatePost(long parentId, byte menuType, string name, string url, string icon, int sort, string isVisible, byte status)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });
            if (!IsAdmin(me.Id)) return Json(new { code = 1, msg = "无权限（仅管理员）" });

            if (string.IsNullOrWhiteSpace(name)) return Json(new { code = 1, msg = "名称不能为空" });

            // switch：选中传 on，不选不传
            bool visible = !string.IsNullOrEmpty(isVisible);

            // 目录：parentId 必须为 0
            if (menuType == 1) parentId = 0;

            // 菜单：url 必填
            if (menuType == 2 && string.IsNullOrWhiteSpace(url))
                return Json(new { code = 1, msg = "菜单Url不能为空" });

            db.Menus.Add(new Menus
            {
                ParentId = parentId,
                MenuType = menuType,
                Name = name.Trim(),
                Url = string.IsNullOrWhiteSpace(url) ? null : url.Trim(),
                Icon = string.IsNullOrWhiteSpace(icon) ? null : icon.Trim(),
                Sort = sort,
                IsVisible = visible,
                Status = status,
                IsDeleted = false,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });

            db.SaveChanges();
            return Json(new { code = 0, msg = "ok" });
        }

        // 编辑菜单页
        public ActionResult MenuEdit(long id)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Content("未登录");
            if (!IsAdmin(me.Id)) return Content("无权限（仅管理员）");

            var m = db.Menus.FirstOrDefault(x => x.Id == id && x.IsDeleted == false);
            if (m == null) return Content("菜单不存在");

            ViewBag.Roots = db.Menus.Where(x => x.IsDeleted == false && x.ParentId == 0 && x.MenuType == (byte)1)
                .OrderBy(x => x.Sort).ThenBy(x => x.Id).ToList();

            return View(m);
        }

        [HttpPost]
        public ActionResult MenuEditPost(long id, long parentId, string name, string url, string icon, int sort, string isVisible, byte status)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });
            if (!IsAdmin(me.Id)) return Json(new { code = 1, msg = "无权限（仅管理员）" });

            var m = db.Menus.FirstOrDefault(x => x.Id == id && x.IsDeleted == false);
            if (m == null) return Json(new { code = 1, msg = "菜单不存在" });

            if (string.IsNullOrWhiteSpace(name)) return Json(new { code = 1, msg = "名称不能为空" });

            bool visible = !string.IsNullOrEmpty(isVisible);

            // 目录固定 parentId=0
            if (m.MenuType == 1) parentId = 0;

            // 菜单 url 必填
            if (m.MenuType == 2 && string.IsNullOrWhiteSpace(url))
                return Json(new { code = 1, msg = "菜单Url不能为空" });

            m.ParentId = parentId;
            m.Name = name.Trim();
            m.Url = string.IsNullOrWhiteSpace(url) ? null : url.Trim();
            m.Icon = string.IsNullOrWhiteSpace(icon) ? null : icon.Trim();
            m.Sort = sort;
            m.IsVisible = visible;
            m.Status = status;
            m.UpdatedAt = DateTime.Now;

            db.SaveChanges();
            return Json(new { code = 0, msg = "ok" });
        }

        // 启用/禁用
        [HttpPost]
        public ActionResult MenuSetStatus(long id, byte status)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });
            if (!IsAdmin(me.Id)) return Json(new { code = 1, msg = "无权限（仅管理员）" });

            var m = db.Menus.FirstOrDefault(x => x.Id == id && x.IsDeleted == false);
            if (m == null) return Json(new { code = 1, msg = "菜单不存在" });

            m.Status = status;
            m.UpdatedAt = DateTime.Now;
            db.SaveChanges();

            // （可选）审计
            // AuditService.Add(db, "Menu", m.Id, "SET_STATUS", me, "菜单状态变更：" + m.Name + " -> " + status);

            return Json(new { code = 0, msg = "ok" });
        }

        // 删除（软删）（可选）
        [HttpPost]
        public ActionResult MenuDelete(long id)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });
            if (!IsAdmin(me.Id)) return Json(new { code = 1, msg = "无权限（仅管理员）" });

            var m = db.Menus.FirstOrDefault(x => x.Id == id && x.IsDeleted == false);
            if (m == null) return Json(new { code = 1, msg = "菜单不存在" });

            // 如果是目录且有子菜单，不允许删（避免脏数据）
            if (m.MenuType == 1 && db.Menus.Any(x => x.IsDeleted == false && x.ParentId == m.Id))
                return Json(new { code = 1, msg = "该目录下存在子菜单，请先处理子菜单" });

            m.IsDeleted = true;
            m.UpdatedAt = DateTime.Now;
            db.SaveChanges();

            return Json(new { code = 0, msg = "ok" });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }


        [HttpGet]
        public ActionResult MenuTreeListJson()
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" }, JsonRequestBehavior.AllowGet);
            if (!IsAdmin(me.Id)) return Json(new { code = 1, msg = "无权限（仅管理员）" }, JsonRequestBehavior.AllowGet);

            var menus = db.Menus
                .Where(m => m.IsDeleted == false && (m.MenuType == (byte)1 || m.MenuType == (byte)2))
                .OrderBy(m => m.Sort).ThenBy(m => m.Id)
                .ToList();

            // 先分组
            var lookup = menus.GroupBy(x => x.ParentId)
                              .ToDictionary(g => g.Key, g => g.ToList());

            var result = new System.Collections.Generic.List<MenuRowVM>();

            void dfs(long parentId, int level)
            {
                if (!lookup.ContainsKey(parentId)) return;

                foreach (var m in lookup[parentId].OrderBy(x => x.Sort).ThenBy(x => x.Id))
                {
                    bool hasChildren = lookup.ContainsKey(m.Id) && lookup[m.Id].Any();

                    result.Add(new MenuRowVM
                    {
                        Id = m.Id,
                        ParentId = m.ParentId,
                        MenuType = m.MenuType,
                        Name = m.Name,
                        Url = m.Url,
                        Icon = m.Icon,
                        Sort = m.Sort,
                        IsVisible = m.IsVisible,
                        Status = m.Status,
                        Level = level,
                        HasChildren = hasChildren
                    });

                    dfs(m.Id, level + 1);
                }
            }

            dfs(0, 0); // 从根开始

            return Json(new { code = 0, data = result }, JsonRequestBehavior.AllowGet);
        }
    }
}