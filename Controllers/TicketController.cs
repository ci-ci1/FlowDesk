using FlowDesk.Controllers.Filters;
using FlowDesk.Models;
using FlowDesk.Models.ViewModels;
using FlowDesk.Models.ViewModels.FlowDesk.Models.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Web.Mvc;

namespace FlowDesk.Controllers
{
    [LoginAuthorize]
    public class TicketController : Controller
    {
        private readonly FlowDeskEntities db = new FlowDeskEntities();

        #region 工单列表页
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

        [HttpGet]
        public ActionResult ListJson(
    int page = 1,
    int limit = 10,
    string keyword = null,
    byte? status = null,
    byte? priority = null,
    long? categoryId = null,
    long? creatorId = null,
    long? assigneeId = null,
    DateTime? createdFrom = null,
    DateTime? createdTo = null,
    string quick = null // 新增：快捷筛选
)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" }, JsonRequestBehavior.AllowGet);

            var q = db.Tickets.Where(t => t.IsDeleted == false);

            // ====== 数据权限：admin 全部；非 admin 只能看我创建或指派给我的 ======
            bool isAdmin = IsAdmin(me.Id);
            long myId = me.Id;

            if (!isAdmin)
            {
                q = q.Where(t => t.CreatorId == myId || t.AssigneeId == myId);
            }

            // ====== 快捷筛选（在普通筛选之前处理） ======
            if (!string.IsNullOrWhiteSpace(quick))
            {
                if (quick == "myCreated")
                {
                    creatorId = myId;
                    // 可选：你也可以清空 assigneeId，避免叠加
                    // assigneeId = null;
                }
                else if (quick == "myTodo")
                {
                    assigneeId = myId;

                    // B：待我处理 = 指派给我 且 状态在(1,2,3)
                    q = q.Where(t => t.Status == 1 || t.Status == 2 || t.Status == 3);

                    // 如果你想 A：待我处理=指派给我（不管状态），删掉上面这一行即可
                }
            }

            // ====== 非 admin 防越权（允许 assigneeId=0 查未指派，但最终仍受数据权限约束）======
            if (!isAdmin)
            {
                if (creatorId.HasValue && creatorId.Value != myId) creatorId = -1;

                // 注意：0 表示未指派，允许
                if (assigneeId.HasValue && assigneeId.Value != 0 && assigneeId.Value != myId) assigneeId = -1;
            }

            // ====== 过滤条件 ======
            if (!string.IsNullOrWhiteSpace(keyword))
                q = q.Where(t => t.Title.Contains(keyword) || t.Code.Contains(keyword));

            if (status.HasValue) q = q.Where(t => t.Status == status.Value);
            if (priority.HasValue) q = q.Where(t => t.Priority == priority.Value);
            if (categoryId.HasValue) q = q.Where(t => t.CategoryId == categoryId.Value);

            if (creatorId.HasValue) q = q.Where(t => t.CreatorId == creatorId.Value);

            // assigneeId=0 => 未指派
            if (assigneeId.HasValue)
            {
                if (assigneeId.Value == 0) q = q.Where(t => t.AssigneeId == null);
                else q = q.Where(t => t.AssigneeId == assigneeId.Value);
            }

            if (createdFrom.HasValue) q = q.Where(t => t.CreatedAt >= createdFrom.Value);
            if (createdTo.HasValue) q = q.Where(t => t.CreatedAt <= createdTo.Value);

            var total = q.Count();

            // ====== join 用户名（避免 N+1）======
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

        #region 新建工单
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
        public ActionResult Report()
        {
            return View();
        }

        [HttpGet]
        public ActionResult ReportData(int days = 7)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" }, JsonRequestBehavior.AllowGet);

            if (days != 7 && days != 30) days = 7;

            var from = DateTime.Today.AddDays(-(days - 1));
            var to = DateTime.Today.AddDays(1);

            var q = db.Tickets.Where(t => t.IsDeleted == false);

            var byStatus = q.GroupBy(t => t.Status)
                .Select(g => new { Status = g.Key, Cnt = g.Count() })
                .ToList();

            var trend = q.Where(t => t.CreatedAt >= from && t.CreatedAt < to)
                .GroupBy(t => System.Data.Entity.DbFunctions.TruncateTime(t.CreatedAt))
                .Select(g => new { Day = g.Key, Cnt = g.Count() })
                .ToList();

            var daysArr = Enumerable.Range(0, days)
                .Select(i => from.AddDays(i))
                .ToList();

            var trendFull = daysArr.Select(d => new
            {
                Day = d.ToString("yyyy-MM-dd"),
                Cnt = trend.Where(x => x.Day.HasValue && x.Day.Value == d).Select(x => x.Cnt).FirstOrDefault()
            }).ToList();

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

        #region 详情页
        public ActionResult Detail(long id)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return RedirectToAction("LoginIndex", "Login");

            var ticket = db.Tickets.FirstOrDefault(t => t.Id == id && t.IsDeleted == false);
            if (ticket == null) return Content("工单不存在");

            // 评论：join Users 拿姓名
            var commentList = (from c in db.TicketComments
                               join u in db.Users on c.UserId equals u.Id
                               where c.TicketId == id
                               orderby c.CreatedAt descending
                               select new TicketCommentVM
                               {
                                   Id = c.Id,
                                   TicketId = c.TicketId,
                                   UserId = c.UserId,
                                   UserName = u.RealName + "(" + u.UserName + ")",
                                   Content = c.Content,
                                   CreatedAt = c.CreatedAt
                               }).ToList();

            // 流转日志：join Users 拿操作人姓名（有些操作人可能被删除，用 left join 更稳）
            var flowList = (from f in db.TicketFlowLogs
                            join u0 in db.Users on f.OperatorId equals u0.Id into fu
                            from u in fu.DefaultIfEmpty()
                            where f.TicketId == id
                            orderby f.CreatedAt descending
                            select new TicketFlowLogVM
                            {
                                Id = f.Id,
                                TicketId = f.TicketId,
                                FromStatus = f.FromStatus,
                                ToStatus = f.ToStatus,
                                Action = f.Action,
                                OperatorId = f.OperatorId,
                                OperatorName = (u == null ? ("UID:" + f.OperatorId) : (u.RealName + "(" + u.UserName + ")")),
                                Remark = f.Remark,
                                CreatedAt = f.CreatedAt
                            }).ToList();

            var vm = new TicketDetailVM
            {
                Ticket = ticket,
                Creator = db.Users.FirstOrDefault(u => u.Id == ticket.CreatorId),
                Assignee = ticket.AssigneeId == null ? null : db.Users.FirstOrDefault(u => u.Id == ticket.AssigneeId),
                Category = ticket.CategoryId == null ? null : db.TicketCategories.FirstOrDefault(c => c.Id == ticket.CategoryId),

                Comments = commentList,
                FlowLogs = flowList
            };

            ViewBag.MeId = me.Id;
            ViewBag.IsAdmin = IsAdmin(me.Id);
            ViewBag.CanOperate = CanOperateTicket(ticket, me.Id);

            ViewBag.RelAssets = (from at in db.AssetTickets
                                 join a in db.Assets on at.AssetId equals a.Id
                                 join t0 in db.AssetTypes on a.TypeId equals t0.Id into atype
                                 from ty in atype.DefaultIfEmpty()
                                 where at.TicketId == id && a.IsDeleted == false
                                 orderby a.AssetNo
                                 select new
                                 {
                                     a.Id,
                                     a.AssetNo,
                                     a.Name,
                                     TypeName = (ty == null ? "" : ty.Name),
                                     a.Status
                                 }).ToList();

            return View(vm);
        }
        #endregion

        #region 评论
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

        #region 指派
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

        #region 状态流转：受理/处理/完成/关闭
        [HttpPost]
        public ActionResult Accept(long ticketId)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });

            var ticket = db.Tickets.FirstOrDefault(t => t.Id == ticketId && t.IsDeleted == false);
            if (ticket == null) return Json(new { code = 1, msg = "工单不存在" });

            if (ticket.Status != (byte)1)
                return Json(new { code = 1, msg = "当前状态不允许受理" });

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

        [HttpPost]
        public ActionResult Close(long ticketId, string remark = null)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });

            var ticket = db.Tickets.FirstOrDefault(t => t.Id == ticketId && t.IsDeleted == false);
            if (ticket == null) return Json(new { code = 1, msg = "工单不存在" });

            if (ticket.Status == (byte)6)
                return Json(new { code = 1, msg = "工单已关闭" });

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

        #region 权限与日志
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

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }


        #region 附件（本地存储）
        private bool CanViewOrUploadTicket(Tickets ticket, long userId)
        {
            if (ticket == null) return false;
            if (IsAdmin(userId)) return true;
            if (ticket.CreatorId == userId) return true;
            if (ticket.AssigneeId.HasValue && ticket.AssigneeId.Value == userId) return true;
            return false;
        }

        // 上传附件
        [HttpPost]
        public ActionResult UploadAttachment(long ticketId)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });

            var ticket = db.Tickets.FirstOrDefault(t => t.Id == ticketId && t.IsDeleted == false);
            if (ticket == null) return Json(new { code = 1, msg = "工单不存在" });

            if (!CanViewOrUploadTicket(ticket, me.Id))
                return Json(new { code = 1, msg = "无权限上传附件" });

            if (Request.Files == null || Request.Files.Count == 0)
                return Json(new { code = 1, msg = "请选择文件" });

            var file = Request.Files[0];
            if (file == null || file.ContentLength <= 0)
                return Json(new { code = 1, msg = "文件为空" });

            // 限制大小：10MB（你可以改）
            var maxSize = 10 * 1024 * 1024;
            if (file.ContentLength > maxSize)
                return Json(new { code = 1, msg = "文件过大，最大 10MB" });

            // 简单白名单（可扩展）
            var ext = Path.GetExtension(file.FileName)?.ToLower();
            string[] allow = new[] { ".png", ".jpg", ".jpeg", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt", ".zip" };
            if (string.IsNullOrEmpty(ext) || !allow.Contains(ext))
                return Json(new { code = 1, msg = "不支持的文件类型" });

            // 保存路径（相对路径 + 物理路径）
            var dirRelative = "/Uploads/Tickets/" + ticketId;
            var dirPhysical = Server.MapPath("~" + dirRelative);
            if (!Directory.Exists(dirPhysical)) Directory.CreateDirectory(dirPhysical);

            var safeName = Path.GetFileName(file.FileName); // 去掉路径
            var saveName = Guid.NewGuid().ToString("N") + "_" + safeName;
            var relativePath = dirRelative + "/" + saveName;
            var physicalPath = Path.Combine(dirPhysical, saveName);

            file.SaveAs(physicalPath);

            // 写库
            var att = new TicketAttachments
            {
                TicketId = ticketId,
                FileName = safeName,
                FilePath = relativePath, // 建议存相对路径
                FileSize = file.ContentLength,
                ContentType = file.ContentType,
                UploadedBy = me.Id,
                CreatedAt = DateTime.Now,
                IsDeleted = false
            };
            db.TicketAttachments.Add(att);

            // 可选：写流转日志（简历加分）
            AddFlowLog(ticketId, ticket.Status, ticket.Status, "UPLOAD", me.Id, "上传附件：" + safeName);

            db.SaveChanges();

            return Json(new { code = 0, msg = "ok" });
        }

        // 附件列表
        [HttpGet]
        public ActionResult AttachmentList(long ticketId)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" }, JsonRequestBehavior.AllowGet);

            var ticket = db.Tickets.FirstOrDefault(t => t.Id == ticketId && t.IsDeleted == false);
            if (ticket == null) return Json(new { code = 1, msg = "工单不存在" }, JsonRequestBehavior.AllowGet);

            if (!CanViewOrUploadTicket(ticket, me.Id))
                return Json(new { code = 1, msg = "无权限查看附件" }, JsonRequestBehavior.AllowGet);

            var list = (from a in db.TicketAttachments
                        where a.TicketId == ticketId && a.IsDeleted == false
                        orderby a.CreatedAt descending
                        select new
                        {
                            a.Id,
                            a.FileName,
                            a.FileSize,
                            a.CreatedAt,
                            a.FilePath
                        }).ToList();

            return Json(new { code = 0, data = list }, JsonRequestBehavior.AllowGet);
        }

        // 下载附件（通过控制器转发更安全）
        [HttpGet]
        public ActionResult DownloadAttachment(long id)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Content("未登录");

            var att = db.TicketAttachments.FirstOrDefault(a => a.Id == id && a.IsDeleted == false);
            if (att == null) return Content("附件不存在");

            var ticket = db.Tickets.FirstOrDefault(t => t.Id == att.TicketId && t.IsDeleted == false);
            if (ticket == null) return Content("工单不存在");

            if (!CanViewOrUploadTicket(ticket, me.Id))
                return Content("无权限下载");

            var physicalPath = Server.MapPath("~" + att.FilePath);
            if (!System.IO.File.Exists(physicalPath))
                return Content("文件不存在");

            return File(physicalPath, "application/octet-stream", att.FileName);
        }

        // 删除附件（管理员或上传者可删，按你需求调整）
        [HttpPost]
        public ActionResult DeleteAttachment(long id)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });

            var att = db.TicketAttachments.FirstOrDefault(a => a.Id == id && a.IsDeleted == false);
            if (att == null) return Json(new { code = 1, msg = "附件不存在" });

            var ticket = db.Tickets.FirstOrDefault(t => t.Id == att.TicketId && t.IsDeleted == false);
            if (ticket == null) return Json(new { code = 1, msg = "工单不存在" });

            bool canDel = IsAdmin(me.Id) || att.UploadedBy == me.Id;
            if (!canDel) return Json(new { code = 1, msg = "无权限删除" });

            att.IsDeleted = true;

            AddFlowLog(ticket.Id, ticket.Status, ticket.Status, "DEL_FILE", me.Id, "删除附件：" + att.FileName);

            db.SaveChanges();
            return Json(new { code = 0, msg = "ok" });
        }
        #endregion



        [HttpGet]
        public ActionResult UserSelectOptions()
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" }, JsonRequestBehavior.AllowGet);

            // 简化：返回所有启用用户（你也可以限制为特定角色）
            var list = db.Users
                .Where(u => u.Status == 1)
                .OrderBy(u => u.Id)
                .Select(u => new
                {
                    u.Id,
                    Name = u.RealName + "(" + u.UserName + ")"
                })
                .ToList();

            return Json(new { code = 0, data = list }, JsonRequestBehavior.AllowGet);
        }

        // 搜索资产（用于绑定弹窗）
        [HttpGet]
        public ActionResult AssetSearch(string keyword, int page = 1, int limit = 10)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" }, JsonRequestBehavior.AllowGet);

            var q = db.Assets.Where(a => a.IsDeleted == false);

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                q = q.Where(a => a.AssetNo.Contains(keyword)
                              || a.Name.Contains(keyword)
                              || a.SerialNo.Contains(keyword));
            }

            var total = q.Count();

            var list = (from a in q
                        join t0 in db.AssetTypes on a.TypeId equals t0.Id into at
                        from t in at.DefaultIfEmpty()
                        join u0 in db.Users on a.OwnerUserId equals u0.Id into ou
                        from u in ou.DefaultIfEmpty()
                        orderby a.CreatedAt descending
                        select new
                        {
                            a.Id,
                            a.AssetNo,
                            a.Name,
                            TypeName = (t == null ? "" : t.Name),
                            a.SerialNo,
                            a.Status,
                            OwnerName = (u == null ? "" : u.RealName)
                        })
                       .Skip((page - 1) * limit)
                       .Take(limit)
                       .ToList();

            return Json(new { code = 0, msg = "", count = total, data = list }, JsonRequestBehavior.AllowGet);
        }

        // 绑定资产到工单
        [HttpPost]
        public ActionResult BindAsset(long ticketId, long assetId)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });

            var ticket = db.Tickets.FirstOrDefault(t => t.Id == ticketId && t.IsDeleted == false);
            if (ticket == null) return Json(new { code = 1, msg = "工单不存在" });

            // 权限：管理员或处理人
            if (!IsAdmin(me.Id) && !CanOperateTicket(ticket, me.Id))
                return Json(new { code = 1, msg = "无权限绑定资产（仅处理人/管理员）" });

            var asset = db.Assets.FirstOrDefault(a => a.Id == assetId && a.IsDeleted == false);
            if (asset == null) return Json(new { code = 1, msg = "资产不存在" });

            // 防重复
            bool exists = db.AssetTickets.Any(x => x.TicketId == ticketId && x.AssetId == assetId);
            if (exists) return Json(new { code = 0, msg = "已绑定" });

            db.AssetTickets.Add(new AssetTickets
            {
                TicketId = ticketId,
                AssetId = assetId,
                CreatedAt = DateTime.Now,
                CreatedBy = me.Id
            });

            // 可选：写工单流转日志（加分）
            AddFlowLog(ticketId, ticket.Status, ticket.Status, "BIND_ASSET", me.Id, "绑定资产：" + asset.AssetNo);

            db.SaveChanges();
            return Json(new { code = 0, msg = "ok" });
        }

        // 解绑资产
        [HttpPost]
        public ActionResult UnbindAsset(long ticketId, long assetId)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });

            var ticket = db.Tickets.FirstOrDefault(t => t.Id == ticketId && t.IsDeleted == false);
            if (ticket == null) return Json(new { code = 1, msg = "工单不存在" });

            if (!IsAdmin(me.Id) && !CanOperateTicket(ticket, me.Id))
                return Json(new { code = 1, msg = "无权限解绑资产（仅处理人/管理员）" });

            var rel = db.AssetTickets.FirstOrDefault(x => x.TicketId == ticketId && x.AssetId == assetId);
            if (rel == null) return Json(new { code = 0, msg = "不存在绑定关系" });

            db.AssetTickets.Remove(rel);

            AddFlowLog(ticketId, ticket.Status, ticket.Status, "UNBIND_ASSET", me.Id, "解绑资产");
            db.SaveChanges();

            return Json(new { code = 0, msg = "ok" });
        }
    }
}