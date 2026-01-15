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
        // 工单列表页
        public ActionResult Index()
        {
            // 下拉用：状态/优先级可以先在前端写死；分类从数据库取
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

            // 过滤条件
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                q = q.Where(t => t.Title.Contains(keyword) || t.Code.Contains(keyword));
            }
            if (status.HasValue) q = q.Where(t => t.Status == status.Value);
            if (priority.HasValue) q = q.Where(t => t.Priority == priority.Value);
            if (categoryId.HasValue) q = q.Where(t => t.CategoryId == categoryId.Value);
            if (createdFrom.HasValue) q = q.Where(t => t.CreatedAt >= createdFrom.Value);
            if (createdTo.HasValue) q = q.Where(t => t.CreatedAt <= createdTo.Value);

            // TODO：下一步再加 RBAC 过滤（普通用户只能看自己创建的等）
            // 先让管理员看到全部，方便你演示

            var total = q.Count();

            var list = q.OrderByDescending(t => t.CreatedAt)
                        .Skip((page - 1) * limit)
                        .Take(limit)
                        .Select(t => new
                        {
                            t.Id,
                            t.Code,
                            t.Title,
                            t.Status,
                            t.Priority,
                            t.CreatedAt,
                            CreatorName = db.Users.Where(u => u.Id == t.CreatorId).Select(u => u.RealName).FirstOrDefault(),
                            AssigneeName = db.Users.Where(u => u.Id == t.AssigneeId).Select(u => u.RealName).FirstOrDefault()
                        })
                        .ToList();

            // Layui table 标准返回：code=0 正常，count=总数，data=数组
            return Json(new { code = 0, msg = "", count = total, data = list }, JsonRequestBehavior.AllowGet);
        }
        #endregion

        #region 新建工单页
        // 新建工单页
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

            // 默认优先级：2=中
            byte pri = priority ?? (byte)2;

            // 用事务保证：Ticket + FlowLog 一起成功
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    var ticket = new Tickets
                    {
                        Code = GenerateTicketCode(),
                        Title = title.Trim(),
                        Description = description,
                        CategoryId = categoryId,
                        Priority = pri,
                        Status = (byte)1, // 新建

                        CreatorId = me.Id,
                        AssigneeId = null,

                        ExpectedAt = expectedAt,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        IsDeleted = false
                    };

                    db.Tickets.Add(ticket);
                    db.SaveChanges(); // 先保存拿到 ticket.Id

                    var flow = new TicketFlowLogs
                    {
                        TicketId = ticket.Id,
                        FromStatus = null,
                        ToStatus = (byte)1,
                        Action = "CREATE",
                        OperatorId = me.Id,
                        Remark = "创建工单",
                        CreatedAt = DateTime.Now
                    };
                    db.TicketFlowLogs.Add(flow);
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

        // 生成工单号：TyyyyMMddNNNN
        private string GenerateTicketCode()
        {
            var date = DateTime.Now.ToString("yyyyMMdd");
            var prefix = "T" + date;

            // 取当天最大 Code，+1
            var last = db.Tickets
                .Where(t => t.Code.StartsWith(prefix))
                .OrderByDescending(t => t.Code)
                .Select(t => t.Code)
                .FirstOrDefault();

            int next = 1;
            if (!string.IsNullOrEmpty(last) && last.Length >= prefix.Length + 4)
            {
                var numStr = last.Substring(prefix.Length); // NNNN
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

            ViewBag.MeId = me.Id; // 前端判断是否本人/是否处理人时用
            return View(vm);
        }

        #endregion

        #region 评论接口
        [HttpPost]
        public ActionResult AddComment(long ticketId, string content)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });

            if (string.IsNullOrWhiteSpace(content))
                return Json(new { code = 1, msg = "评论不能为空" });

            var ticket = db.Tickets.FirstOrDefault(t => t.Id == ticketId && t.IsDeleted == false);
            if (ticket == null) return Json(new { code = 1, msg = "工单不存在" });

            var c = new TicketComments
            {
                TicketId = ticketId,
                UserId = me.Id,
                Content = content.Trim(),
                CreatedAt = DateTime.Now
            };
            db.TicketComments.Add(c);
            db.SaveChanges();

            return Json(new { code = 0, msg = "ok" });
        }
        #endregion

        #region 状态流转接口
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

        #region 指派处理人 Assign
        [HttpPost]
        public ActionResult Assign(long ticketId, long assigneeId)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });

            var ticket = db.Tickets.FirstOrDefault(t => t.Id == ticketId && t.IsDeleted == false);
            if (ticket == null) return Json(new { code = 1, msg = "工单不存在" });

            // 简化权限：先不判断角色，后面加 RBAC
            ticket.AssigneeId = assigneeId;

            AddFlowLog(ticket.Id, ticket.Status, ticket.Status, "ASSIGN", me.Id, "指派处理人");
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

            if (ticket.Status != (byte)1)
                return Json(new { code = 1, msg = "当前状态不允许受理" });

            // 如果没有处理人，默认把当前用户作为处理人
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

            var from = ticket.Status;
            ticket.Status = (byte)5;
            ticket.ResolvedAt = DateTime.Now;

            AddFlowLog(ticket.Id, from, ticket.Status, "DONE", me.Id, string.IsNullOrWhiteSpace(remark) ? "完成工单" : remark);
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

            var from = ticket.Status;
            ticket.Status = (byte)6;
            ticket.ClosedAt = DateTime.Now;

            AddFlowLog(ticket.Id, from, ticket.Status, "CLOSE", me.Id, string.IsNullOrWhiteSpace(remark) ? "关闭工单" : remark);
            db.SaveChanges();

            return Json(new { code = 0, msg = "ok" });
        }
        #endregion


    }
}