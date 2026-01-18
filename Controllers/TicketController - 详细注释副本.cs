using FlowDesk.Controllers.Filters;
using FlowDesk.Models;
using FlowDesk.Models.ViewModels;
using System;
using System.Linq;
using System.Web.Mvc;

namespace FlowDesk.Controllers
{
    [LoginAuthorize]
    public class TicketController : Controller
    {
        private readonly FlowDeskEntities db = new FlowDeskEntities();

        #region 工单列表页
        // ============ 工单列表页 ============
        public ActionResult Index()
        {
            var categories = db.TicketCategories
                .Where(c => c.Status == 1)
                .OrderBy(c => c.Sort)
                .ThenBy(c => c.Id)
                .ToList();

            ViewBag.Categories = categories;
            return View();
        }

        // Layui table 数据接口
        [HttpGet]
        public ActionResult ListJson(int page = 1, int limit = 10,
            string keyword = null,
            byte? status = null,
            byte? priority = null,
            long? categoryId = null,
            DateTime? createdFrom = null,
            DateTime? createdTo = null)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" }, JsonRequestBehavior.AllowGet);

            var q = db.Tickets.Where(t => t.IsDeleted == false);

            //（可选）这里先不做权限过滤，下一步再加
            // if (!IsAdmin(me.Id)) { ... }

            if (!string.IsNullOrWhiteSpace(keyword))
                q = q.Where(t => t.Title.Contains(keyword) || t.Code.Contains(keyword));

            if (status.HasValue) q = q.Where(t => t.Status == status.Value);
            if (priority.HasValue) q = q.Where(t => t.Priority == priority.Value);
            if (categoryId.HasValue) q = q.Where(t => t.CategoryId == categoryId.Value);
            if (createdFrom.HasValue) q = q.Where(t => t.CreatedAt >= createdFrom.Value);
            if (createdTo.HasValue) q = q.Where(t => t.CreatedAt <= createdTo.Value);

            var total = q.Count();

            // join 用户名（避免 N+1）
            var list = (from t in q
                        join cu in db.Users on t.CreatorId equals cu.Id
                        join au0 in db.Users on t.AssigneeId equals au0.Id into au1
                        from au in au1.DefaultIfEmpty()
                        orderby t.CreatedAt descending
                        select new
                        {
                            t.Id,
                            t.Code,
                            t.Title,
                            t.Status,
                            t.Priority,
                            t.CreatedAt,
                            CreatorName = cu.RealName,
                            AssigneeName = (au == null ? "" : au.RealName)
                        })
                       .Skip((page - 1) * limit)
                       .Take(limit)
                       .ToList();

            return Json(new { code = 0, msg = "", count = total, data = list }, JsonRequestBehavior.AllowGet);
        }
        #endregion

        #region 新建工单页
        // ============ 新建工单 ============
        public ActionResult Create()
        {
            var categories = db.TicketCategories
                .Where(c => c.Status == 1)
                .OrderBy(c => c.Sort)
                .ThenBy(c => c.Id)
                .ToList();

            ViewBag.Categories = categories;
            return View();
        }

        [HttpPost]
        public ActionResult CreatePost(string title, string description, long? categoryId, byte? priority, DateTime? expectedAt)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });

            if (string.IsNullOrWhiteSpace(title))
                return Json(new { code = 1, msg = "标题不能为空" });

            byte pri = priority ?? (byte)2;

            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    var now = DateTime.Now;

                    var ticket = new Tickets
                    {
                        Code = GenerateTicketCode(),
                        Title = title.Trim(),
                        Description = description,
                        CategoryId = categoryId,
                        Priority = pri,
                        Status = (byte)1,

                        CreatorId = me.Id,
                        AssigneeId = null,

                        ExpectedAt = expectedAt,
                        CreatedAt = now,
                        UpdatedAt = now,
                        IsDeleted = false
                    };

                    db.Tickets.Add(ticket);
                    db.SaveChanges();

                    AddFlowLog(ticket.Id, null, (byte)1, "CREATE", me.Id, "创建工单");
                    db.SaveChanges();

                    tx.Commit();
                    return Json(new { code = 0, msg = "创建成功", data = new { id = ticket.Id, code = ticket.Code } });
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return Json(new { code = 1, msg = "创建失败：" + ex.Message });
                }
            }
        }

        private string GenerateTicketCode()
        {
            var date = DateTime.Now.ToString("yyyyMMdd");
            var prefix = "T" + date;

            var last = db.Tickets
                .Where(t => t.Code.StartsWith(prefix))
                .OrderByDescending(t => t.Code)
                .Select(t => t.Code)
                .FirstOrDefault();

            int next = 1;
            if (!string.IsNullOrEmpty(last) && last.Length >= prefix.Length + 4)
            {
                var numStr = last.Substring(prefix.Length);
                int num;
                if (int.TryParse(numStr, out num)) next = num + 1;
            }

            return prefix + next.ToString("D4");
        }
        #endregion

        #region 报表页
        // 报表页
        public ActionResult Report()
        {
            return View();
        }

        // 报表数据接口
        [HttpGet]
        public ActionResult ReportData(int days = 7)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" }, JsonRequestBehavior.AllowGet);

            if (days != 7 && days != 30) days = 7;

            var from = DateTime.Today.AddDays(-(days - 1));
            var to = DateTime.Today.AddDays(1); // 到明天 0 点（包含今天）

            var q = db.Tickets.Where(t => t.IsDeleted == false);

            // TODO：后续可加 RBAC：普通用户只看自己创建的等
            // q = q.Where(t => t.CreatorId == me.Id);

            // 1) 状态分布
            var byStatus = q.GroupBy(t => t.Status)
                .Select(g => new { Status = g.Key, Cnt = g.Count() })
                .ToList();

            // 2) 近 N 天趋势（按天统计新增工单）
            // 说明：EF6 对 DateTime.Date 支持不稳定，这里用 DbFunctions.TruncateTime
            var trend = q.Where(t => t.CreatedAt >= from && t.CreatedAt < to)
                .GroupBy(t => System.Data.Entity.DbFunctions.TruncateTime(t.CreatedAt))
                .Select(g => new { Day = g.Key, Cnt = g.Count() })
                .ToList();

            // 补齐缺失日期（前端画折线更好看）
            var daysArr = Enumerable.Range(0, days)
                .Select(i => from.AddDays(i))
                .ToList();

            var trendFull = daysArr.Select(d => new
            {
                Day = d.ToString("yyyy-MM-dd"),
                Cnt = trend.Where(x => x.Day.HasValue && x.Day.Value == d).Select(x => x.Cnt).FirstOrDefault()
            }).ToList();

            // 3) 分类 TOP（没有分类时归为“未分类”）
            var catTop = (from t in q
                          join c in db.TicketCategories on t.CategoryId equals c.Id into tc
                          from c in tc.DefaultIfEmpty()
                          group t by (c == null ? "未分类" : c.Name) into g
                          orderby g.Count() descending
                          select new { Name = g.Key, Cnt = g.Count() })
                         .Take(10)
                         .ToList();

            return Json(new
            {
                code = 0,
                msg = "",
                data = new
                {
                    byStatus,
                    trend = trendFull,
                    catTop
                }
            }, JsonRequestBehavior.AllowGet);
        }
        #endregion

        #region Detail
        // ============ 工单详情 ============
        public ActionResult Detail(long id)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return RedirectToAction("LoginIndex", "Login");

            var ticket = db.Tickets.FirstOrDefault(t => t.Id == id && t.IsDeleted == false);
            if (ticket == null) return Content("工单不存在");

            var vm = new TicketDetailVM
            {
                Ticket = ticket,
                Creator = db.Users.FirstOrDefault(u => u.Id == ticket.CreatorId),
                Assignee = ticket.AssigneeId == null ? null : db.Users.FirstOrDefault(u => u.Id == ticket.AssigneeId),
                Category = ticket.CategoryId == null ? null : db.TicketCategories.FirstOrDefault(c => c.Id == ticket.CategoryId),

                FlowLogs = db.TicketFlowLogs
                    .Where(x => x.TicketId == id)
                    .OrderByDescending(x => x.CreatedAt)
                    .ToList(),

                Comments = db.TicketComments
                    .Where(x => x.TicketId == id)
                    .OrderByDescending(x => x.CreatedAt)
                    .ToList()
            };

            ViewBag.MeId = me.Id;
            ViewBag.IsAdmin = IsAdmin(me.Id);
            ViewBag.CanOperate = CanOperateTicket(ticket, me.Id);

            return View(vm);
        }

        #endregion

        #region 评论接口
        // ============ 评论 ============
        [HttpPost]
        public ActionResult AddComment(long ticketId, string content)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });

            if (string.IsNullOrWhiteSpace(content))
                return Json(new { code = 1, msg = "评论不能为空" });

            var ticket = db.Tickets.FirstOrDefault(t => t.Id == ticketId && t.IsDeleted == false);
            if (ticket == null) return Json(new { code = 1, msg = "工单不存在" });

            db.TicketComments.Add(new TicketComments
            {
                TicketId = ticketId,
                UserId = me.Id,
                Content = content.Trim(),
                CreatedAt = DateTime.Now
            });

            db.SaveChanges();
            return Json(new { code = 0, msg = "ok" });
        }
        #endregion

        #region 指派处理人 Assign
        // ============ 指派（管理员） ============
        [HttpGet]
        public ActionResult UserOptions()
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" }, JsonRequestBehavior.AllowGet);

            if (!IsAdmin(me.Id))
                return Json(new { code = 1, msg = "无权限" }, JsonRequestBehavior.AllowGet);

            var users = db.Users
                .Where(u => u.Status == 1)
                .OrderBy(u => u.Id)
                .Select(u => new { u.Id, Name = u.RealName + "(" + u.UserName + ")" })
                .ToList();

            return Json(new { code = 0, msg = "", data = users }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public ActionResult Assign(long ticketId, long assigneeId)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });

            if (!IsAdmin(me.Id))
                return Json(new { code = 1, msg = "无权限指派" });

            var ticket = db.Tickets.FirstOrDefault(t => t.Id == ticketId && t.IsDeleted == false);
            if (ticket == null) return Json(new { code = 1, msg = "工单不存在" });

            var from = ticket.Status;
            ticket.AssigneeId = assigneeId;

            AddFlowLog(ticket.Id, from, ticket.Status, "ASSIGN", me.Id, "指派处理人");
            db.SaveChanges();

            return Json(new { code = 0, msg = "ok" });
        }
        #endregion

        #region 受理 Accept
        [HttpPost]
        public ActionResult Accept(long ticketId)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });

            var ticket = db.Tickets.FirstOrDefault(t => t.Id == ticketId && t.IsDeleted == false);
            if (ticket == null) return Json(new { code = 1, msg = "工单不存在" });

            // 状态校验
            if (ticket.Status != (byte)1)
                return Json(new { code = 1, msg = "当前状态不允许受理" });

            // 权限校验：管理员或处理人可操作
            // 如果还未指派，允许“当前用户受理并成为处理人”
            if (!IsAdmin(me.Id))
            {
                if (ticket.AssigneeId.HasValue && ticket.AssigneeId.Value != me.Id)
                    return Json(new { code = 1, msg = "无权限受理（非处理人）" });
            }

            if (ticket.AssigneeId == null) ticket.AssigneeId = me.Id;

            var from = ticket.Status;
            ticket.Status = (byte)2;
            ticket.AcceptedAt = DateTime.Now;

            AddFlowLog(ticket.Id, from, ticket.Status, "ACCEPT", me.Id, "受理工单");
            db.SaveChanges();

            return Json(new { code = 0, msg = "ok" });
        }
        #endregion

        #region 开始处理 StartProcess
        [HttpPost]
        public ActionResult StartProcess(long ticketId)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });

            var ticket = db.Tickets.FirstOrDefault(t => t.Id == ticketId && t.IsDeleted == false);
            if (ticket == null) return Json(new { code = 1, msg = "工单不存在" });

            if (ticket.Status != (byte)2)
                return Json(new { code = 1, msg = "当前状态不允许开始处理" });

            if (!CanOperateTicket(ticket, me.Id))
                return Json(new { code = 1, msg = "无权限操作（仅处理人/管理员）" });

            var from = ticket.Status;
            ticket.Status = (byte)3;

            AddFlowLog(ticket.Id, from, ticket.Status, "PROCESS", me.Id, "开始处理");
            db.SaveChanges();

            return Json(new { code = 0, msg = "ok" });
        }
        #endregion

        #region 完成 Done
        [HttpPost]
        public ActionResult Done(long ticketId, string remark = null)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });

            var ticket = db.Tickets.FirstOrDefault(t => t.Id == ticketId && t.IsDeleted == false);
            if (ticket == null) return Json(new { code = 1, msg = "工单不存在" });

            if (ticket.Status != (byte)3)
                return Json(new { code = 1, msg = "当前状态不允许完成" });

            if (!CanOperateTicket(ticket, me.Id))
                return Json(new { code = 1, msg = "无权限操作（仅处理人/管理员）" });

            var from = ticket.Status;
            ticket.Status = (byte)5;
            ticket.ResolvedAt = DateTime.Now;

            AddFlowLog(ticket.Id, from, ticket.Status, "DONE", me.Id,
                string.IsNullOrWhiteSpace(remark) ? "完成工单" : remark.Trim());

            db.SaveChanges();
            return Json(new { code = 0, msg = "ok" });
        }
        #endregion

        #region 关闭Close
        [HttpPost]
        public ActionResult Close(long ticketId, string remark = null)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });

            var ticket = db.Tickets.FirstOrDefault(t => t.Id == ticketId && t.IsDeleted == false);
            if (ticket == null) return Json(new { code = 1, msg = "工单不存在" });

            if (ticket.Status == (byte)6)
                return Json(new { code = 1, msg = "工单已关闭" });

            // 关闭权限：管理员 or 处理人
            if (!CanOperateTicket(ticket, me.Id))
                return Json(new { code = 1, msg = "无权限关闭（仅处理人/管理员）" });

            var from = ticket.Status;
            ticket.Status = (byte)6;
            ticket.ClosedAt = DateTime.Now;

            AddFlowLog(ticket.Id, from, ticket.Status, "CLOSE", me.Id,
                string.IsNullOrWhiteSpace(remark) ? "关闭工单" : remark.Trim());

            db.SaveChanges();
            return Json(new { code = 0, msg = "ok" });
        }
        #endregion

        #region 权限工具
        // ============ 私有方法：权限 / 流转日志 ============
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

        private bool CanOperateTicket(Tickets ticket, long userId)
        {
            if (ticket == null) return false;
            if (IsAdmin(userId)) return true;
            if (ticket.AssigneeId.HasValue && ticket.AssigneeId.Value == userId) return true;
            return false;
        }

        private void AddFlowLog(long ticketId, byte? fromStatus, byte toStatus, string action, long operatorId, string remark)
        {
            db.TicketFlowLogs.Add(new TicketFlowLogs
            {
                TicketId = ticketId,
                FromStatus = fromStatus,
                ToStatus = toStatus,
                Action = action,
                OperatorId = operatorId,
                Remark = remark,
                CreatedAt = DateTime.Now
            });
        }
        #endregion
    }
}