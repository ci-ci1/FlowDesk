using FlowDesk.Controllers.Filters;
using FlowDesk.Models;
using FlowDesk.Models.ViewModels;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using FlowDesk.Services;

namespace FlowDesk.Controllers
{
    [LoginAuthorize]
    public class AssetController : Controller
    {
        private readonly FlowDeskEntities db = new FlowDeskEntities();
        private string J(object obj) => new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(obj);

        #region 列表页
        public ActionResult Index()
        {
            ViewBag.Types = db.AssetTypes.Where(t => t.Status == 1)
                .OrderBy(t => t.Sort).ThenBy(t => t.Id).ToList();

            ViewBag.Users = db.Users.Where(u => u.Status == 1)
                .OrderBy(u => u.Id).ToList();

            return View();
        }

        [HttpGet]
        public ActionResult ListJson(int page = 1, int limit = 10,
            string keyword = null,
            long? typeId = null,
            byte? status = null,
            long? ownerUserId = null)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" }, JsonRequestBehavior.AllowGet);

            var q = db.Assets.Where(a => a.IsDeleted == false);

            // 数据权限：admin 全量；非 admin 仅看自己名下资产
            bool isAdmin = IsAdmin(me.Id);
            if (!isAdmin)
            {
                long myId = me.Id;
                q = q.Where(a => a.OwnerUserId == myId);
                // 防越权：非 admin 传 ownerUserId 不是我 -> 返回空
                if (ownerUserId.HasValue && ownerUserId.Value != myId) ownerUserId = -1;
            }

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                q = q.Where(a => a.AssetNo.Contains(keyword)
                              || a.Name.Contains(keyword)
                              || a.SerialNo.Contains(keyword));
            }
            if (typeId.HasValue) q = q.Where(a => a.TypeId == typeId.Value);
            if (status.HasValue) q = q.Where(a => a.Status == status.Value);

            // ownerUserId 过滤：0 表示“未领用/在库（OwnerUserId 为空）”
            if (ownerUserId.HasValue)
            {
                if (ownerUserId.Value == 0) q = q.Where(a => a.OwnerUserId == null);
                else q = q.Where(a => a.OwnerUserId == ownerUserId.Value);
            }

            var total = q.Count();

            // join 类型、用户显示名（避免 N+1）
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
                            a.Brand,
                            a.Model,
                            a.SerialNo,
                            a.Status,
                            OwnerName = (u == null ? "" : u.RealName),
                            a.Location,
                            a.CreatedAt
                        })
                        .Skip((page - 1) * limit)
                        .Take(limit)
                        .ToList();

            return Json(new { code = 0, msg = "", count = total, data = list }, JsonRequestBehavior.AllowGet);
        }
        #endregion

        #region 入库
        public ActionResult Create()
        {
            ViewBag.Types = db.AssetTypes.Where(t => t.Status == 1)
                .OrderBy(t => t.Sort).ThenBy(t => t.Id).ToList();
            return View();
        }

        [HttpPost]
        public ActionResult CreatePost(string assetNo, string name, long? typeId,
            string brand, string model, string serialNo, DateTime? purchaseDate,
            string dept, string location, string remark)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });

            if (string.IsNullOrWhiteSpace(assetNo)) return Json(new { code = 1, msg = "资产编号不能为空" });
            if (string.IsNullOrWhiteSpace(name)) return Json(new { code = 1, msg = "资产名称不能为空" });

            assetNo = assetNo.Trim();

            if (db.Assets.Any(a => a.IsDeleted == false && a.AssetNo == assetNo))
                return Json(new { code = 1, msg = "资产编号已存在" });

            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    var now = DateTime.Now;

                    var a = new Assets
                    {
                        AssetNo = assetNo,
                        Name = name.Trim(),
                        TypeId = typeId,
                        Brand = brand,
                        Model = model,
                        SerialNo = serialNo,
                        PurchaseDate = purchaseDate,
                        Status = (byte)1, // 在库
                        Dept = dept,
                        OwnerUserId = null,
                        Location = location,
                        Remark = remark,
                        CreatedAt = now,
                        UpdatedAt = now,
                        IsDeleted = false
                    };

                    db.Assets.Add(a);
                    db.SaveChanges();

                    AddOpLog(a.Id, 1, null, a.Status, null, null, me.Id, "入库");

                    AuditService.Add(db, "Asset", a.Id, "CREATE", me,
    "资产入库：" + a.AssetNo + "；名称：" + a.Name);

                    db.SaveChanges();

                    tx.Commit();
                    return Json(new { code = 0, msg = "ok" });
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return Json(new { code = 1, msg = "入库失败：" + ex.Message });
                }
            }
        }
        #endregion

        #region 详情
        public ActionResult Detail(long id)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return RedirectToAction("LoginIndex", "Login");

            var asset = db.Assets.FirstOrDefault(a => a.Id == id && a.IsDeleted == false);
            if (asset == null) return Content("资产不存在");

            bool isAdmin = IsAdmin(me.Id);
            if (!isAdmin)
            {
                if (asset.OwnerUserId != me.Id) return Content("无权限查看该资产");
            }

            ViewBag.Type = asset.TypeId == null ? null : db.AssetTypes.FirstOrDefault(t => t.Id == asset.TypeId);
            ViewBag.Owner = asset.OwnerUserId == null ? null : db.Users.FirstOrDefault(u => u.Id == asset.OwnerUserId);

            ViewBag.IsAdmin = isAdmin;
            ViewBag.MeId = me.Id;

            ViewBag.Users = db.Users.Where(u => u.Status == 1)
                .OrderBy(u => u.Id).ToList();

            ViewBag.Logs = (from l in db.AssetOpLogs
                            join ou0 in db.Users on l.OperatorId equals ou0.Id into ou1
                            from ou in ou1.DefaultIfEmpty()
                            join fu0 in db.Users on l.FromOwnerId equals fu0.Id into fu1
                            from fu in fu1.DefaultIfEmpty()
                            join tu0 in db.Users on l.ToOwnerId equals tu0.Id into tu1
                            from tu in tu1.DefaultIfEmpty()
                            where l.AssetId == id
                            orderby l.CreatedAt descending
                            select new AssetOpLogVM
                            {
                                Id = l.Id,
                                OpType = l.OpType,
                                FromStatus = l.FromStatus,
                                ToStatus = l.ToStatus,
                                FromOwnerName = (fu == null ? "" : fu.RealName),
                                ToOwnerName = (tu == null ? "" : tu.RealName),
                                OperatorName = (ou == null ? "" : ou.RealName),
                                Remark = l.Remark,
                                CreatedAt = l.CreatedAt
                            }).ToList();

            ViewBag.RelTickets = (from at in db.AssetTickets
                                  join t in db.Tickets on at.TicketId equals t.Id
                                  where at.AssetId == id && t.IsDeleted == false
                                  orderby t.CreatedAt descending
                                  select new
                                  {
                                      t.Id,
                                      t.Code,
                                      t.Title,
                                      t.Status,
                                      t.CreatedAt
                                  }).Take(20).ToList();

            return View(asset);
        }
        #endregion

        #region 资产操作：领用/归还/报废/维修
        // 领用：在库(1) -> 使用中(2)，设置 OwnerUserId
        [HttpPost]
        public ActionResult Checkout(long assetId, long toOwnerId, string remark = null)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });

            if (!IsAdmin(me.Id)) return Json(new { code = 1, msg = "无权限（仅管理员）" });

            var a = db.Assets.FirstOrDefault(x => x.Id == assetId && x.IsDeleted == false);
            if (a == null) return Json(new { code = 1, msg = "资产不存在" });

            if (a.Status != (byte)1) return Json(new { code = 1, msg = "仅在库资产可领用" });

            var fromStatus = a.Status;
            var fromOwner = a.OwnerUserId;
            var beforeJson = J(new
            {
                a.Id,
                a.AssetNo,
                Status = a.Status,
                OwnerUserId = a.OwnerUserId
            });
            a.OwnerUserId = toOwnerId;
            a.Status = (byte)2; // 使用中

            AddOpLog(a.Id, 2, fromStatus, a.Status, fromOwner, a.OwnerUserId, me.Id,
                string.IsNullOrWhiteSpace(remark) ? "领用" : remark.Trim());

            // ✅ 写审计（SaveChanges 前）
            AuditService.Add(db, "Asset", a.Id, "CHECKOUT", me,
                "资产领用：" + a.AssetNo + " -> OwnerId=" + toOwnerId);
            var afterJson = J(new
            {
                a.Id,
                a.AssetNo,
                Status = a.Status,
                OwnerUserId = a.OwnerUserId
            });

            AuditService.Add(db, "Asset", a.Id, "CHECKOUT", me,
                "资产领用：" + a.AssetNo + " -> OwnerId=" + toOwnerId,
                beforeJson: beforeJson,
                afterJson: afterJson);
            db.SaveChanges();
            return Json(new { code = 0, msg = "ok" });
        }

        // 归还：使用中(2) -> 在库(1)，OwnerUserId=null
        [HttpPost]
        public ActionResult Return(long assetId, string remark = null)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });

            if (!IsAdmin(me.Id)) return Json(new { code = 1, msg = "无权限（仅管理员）" });

            var a = db.Assets.FirstOrDefault(x => x.Id == assetId && x.IsDeleted == false);
            if (a == null) return Json(new { code = 1, msg = "资产不存在" });

            if (a.Status != (byte)2) return Json(new { code = 1, msg = "仅使用中资产可归还" });
            var beforeJson = J(new
            {
                a.Id,
                a.AssetNo,
                Status = a.Status,
                OwnerUserId = a.OwnerUserId
            });
            var fromStatus = a.Status;
            var fromOwner = a.OwnerUserId;

            a.OwnerUserId = null;
            a.Status = (byte)1;

            AddOpLog(a.Id, 3, fromStatus, a.Status, fromOwner, null, me.Id,
                string.IsNullOrWhiteSpace(remark) ? "归还" : remark.Trim());

            AuditService.Add(db, "Asset", a.Id, "RETURN", me,
    "资产归还：" + a.AssetNo);
            var afterJson = J(new
            {
                a.Id,
                a.AssetNo,
                Status = a.Status,
                OwnerUserId = a.OwnerUserId
            });

            AuditService.Add(db, "Asset", a.Id, "RETURN", me,
                "资产归还：" + a.AssetNo,
                beforeJson: beforeJson,
                afterJson: afterJson);
            db.SaveChanges();
            return Json(new { code = 0, msg = "ok" });
        }

        // 报废：任意非报废 -> 报废(4)
        [HttpPost]
        public ActionResult Scrap(long assetId, string remark = null)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });

            if (!IsAdmin(me.Id)) return Json(new { code = 1, msg = "无权限（仅管理员）" });

            var a = db.Assets.FirstOrDefault(x => x.Id == assetId && x.IsDeleted == false);
            if (a == null) return Json(new { code = 1, msg = "资产不存在" });

            if (a.Status == (byte)4) return Json(new { code = 1, msg = "资产已报废" });
            var beforeJson = J(new
            {
                a.Id,
                a.AssetNo,
                Status = a.Status,
                OwnerUserId = a.OwnerUserId
            });
            var fromStatus = a.Status;
            var fromOwner = a.OwnerUserId;

            a.Status = (byte)4;
            a.OwnerUserId = null;

            AddOpLog(a.Id, 4, fromStatus, a.Status, fromOwner, null, me.Id,
                string.IsNullOrWhiteSpace(remark) ? "报废" : remark.Trim());
            AuditService.Add(db, "Asset", a.Id, "SCRAP", me,
    "资产报废：" + a.AssetNo + "；备注：" + (string.IsNullOrWhiteSpace(remark) ? "-" : remark.Trim()));
            var afterJson = J(new
            {
                a.Id,
                a.AssetNo,
                Status = a.Status,
                OwnerUserId = a.OwnerUserId
            });

            AuditService.Add(db, "Asset", a.Id, "SCRAP", me,
                "资产报废：" + a.AssetNo + "；备注：" + (string.IsNullOrWhiteSpace(remark) ? "-" : remark.Trim()),
                beforeJson: beforeJson,
                afterJson: afterJson);
            db.SaveChanges();
            return Json(new { code = 0, msg = "ok" });
        }

        // 维修：任意非报废 -> 维修中(3)
        [HttpPost]
        public ActionResult Repair(long assetId, string remark = null)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });

            if (!IsAdmin(me.Id)) return Json(new { code = 1, msg = "无权限（仅管理员）" });

            var a = db.Assets.FirstOrDefault(x => x.Id == assetId && x.IsDeleted == false);
            if (a == null) return Json(new { code = 1, msg = "资产不存在" });

            if (a.Status == (byte)4) return Json(new { code = 1, msg = "报废资产不可维修" });

            var fromStatus = a.Status;
            var fromOwner = a.OwnerUserId;

            a.Status = (byte)3;

            AddOpLog(a.Id, 5, fromStatus, a.Status, fromOwner, fromOwner, me.Id,
                string.IsNullOrWhiteSpace(remark) ? "维修" : remark.Trim());
            AuditService.Add(db, "Asset", a.Id, "REPAIR", me,
    "资产维修：" + a.AssetNo + "；备注：" + (string.IsNullOrWhiteSpace(remark) ? "-" : remark.Trim()));
            db.SaveChanges();
            return Json(new { code = 0, msg = "ok" });
        }
        #endregion

        #region 工具方法：权限/日志
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

        private void AddOpLog(long assetId, byte opType, byte? fromStatus, byte? toStatus,
            long? fromOwnerId, long? toOwnerId, long operatorId, string remark)
        {
            db.AssetOpLogs.Add(new AssetOpLogs
            {
                AssetId = assetId,
                OpType = opType,
                FromStatus = fromStatus,
                ToStatus = toStatus,
                FromOwnerId = fromOwnerId,
                ToOwnerId = toOwnerId,
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


        #region 导入导出（Excel）

        // 下载导入模板
        [HttpGet]
        public ActionResult ImportTemplate()
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Content("未登录");

            // 仅管理员允许批量入库（按你的规则）
            if (!IsAdmin(me.Id)) return Content("无权限（仅管理员）");

            IWorkbook wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet("Assets");

            var header = sheet.CreateRow(0);
            header.CreateCell(0).SetCellValue("AssetNo(必填)");
            header.CreateCell(1).SetCellValue("Name(必填)");
            header.CreateCell(2).SetCellValue("TypeName(可选,如:笔记本)");
            header.CreateCell(3).SetCellValue("Brand(可选)");
            header.CreateCell(4).SetCellValue("Model(可选)");
            header.CreateCell(5).SetCellValue("SerialNo(可选)");
            header.CreateCell(6).SetCellValue("PurchaseDate(可选,yyyy-MM-dd)");
            header.CreateCell(7).SetCellValue("Dept(可选)");
            header.CreateCell(8).SetCellValue("Location(可选)");
            header.CreateCell(9).SetCellValue("Remark(可选)");

            // 示例行
            var row1 = sheet.CreateRow(1);
            row1.CreateCell(0).SetCellValue("IT-LAP-001");
            row1.CreateCell(1).SetCellValue("ThinkPad X1 Carbon");
            row1.CreateCell(2).SetCellValue("笔记本");
            row1.CreateCell(3).SetCellValue("Lenovo");
            row1.CreateCell(4).SetCellValue("X1 Carbon");
            row1.CreateCell(5).SetCellValue("SN123456");
            row1.CreateCell(6).SetCellValue("2026-01-01");
            row1.CreateCell(7).SetCellValue("IT");
            row1.CreateCell(8).SetCellValue("A座 3F");
            row1.CreateCell(9).SetCellValue("示例数据");

            for (int i = 0; i <= 9; i++) sheet.AutoSizeColumn(i);

            using (var ms = new MemoryStream())
            {
                wb.Write(ms);
                var bytes = ms.ToArray();
                return File(bytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "Asset_Import_Template.xlsx");
            }
        }

        // 导出（按筛选条件导出）
        [HttpGet]
        public ActionResult Export(string keyword = null, long? typeId = null, byte? status = null, long? ownerUserId = null)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Content("未登录");

            var q = db.Assets.Where(a => a.IsDeleted == false);

            bool isAdmin = IsAdmin(me.Id);
            long myId = me.Id;
            if (!isAdmin)
            {
                q = q.Where(a => a.OwnerUserId == myId);
                if (ownerUserId.HasValue && ownerUserId.Value != myId) ownerUserId = -1;
            }

            if (!string.IsNullOrWhiteSpace(keyword))
                q = q.Where(a => a.AssetNo.Contains(keyword) || a.Name.Contains(keyword) || a.SerialNo.Contains(keyword));

            if (typeId.HasValue) q = q.Where(a => a.TypeId == typeId.Value);
            if (status.HasValue) q = q.Where(a => a.Status == status.Value);

            if (ownerUserId.HasValue)
            {
                if (ownerUserId.Value == 0) q = q.Where(a => a.OwnerUserId == null);
                else q = q.Where(a => a.OwnerUserId == ownerUserId.Value);
            }

            var data = (from a in q
                        join t0 in db.AssetTypes on a.TypeId equals t0.Id into at
                        from t in at.DefaultIfEmpty()
                        join u0 in db.Users on a.OwnerUserId equals u0.Id into ou
                        from u in ou.DefaultIfEmpty()
                        orderby a.AssetNo
                        select new
                        {
                            a.AssetNo,
                            a.Name,
                            TypeName = (t == null ? "" : t.Name),
                            a.Brand,
                            a.Model,
                            a.SerialNo,
                            a.PurchaseDate,
                            a.Status,
                            OwnerName = (u == null ? "" : (u.RealName + "(" + u.UserName + ")")),
                            a.Dept,
                            a.Location,
                            a.Remark,
                            a.CreatedAt
                        }).ToList();

            IWorkbook wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet("Assets");

            var header = sheet.CreateRow(0);
            string[] cols = new[]
            {
        "AssetNo","Name","Type","Brand","Model","SerialNo","PurchaseDate","Status","Owner","Dept","Location","Remark","CreatedAt"
    };
            for (int i = 0; i < cols.Length; i++) header.CreateCell(i).SetCellValue(cols[i]);

            for (int i = 0; i < data.Count; i++)
            {
                var r = sheet.CreateRow(i + 1);
                var x = data[i];
                r.CreateCell(0).SetCellValue(x.AssetNo);
                r.CreateCell(1).SetCellValue(x.Name);
                r.CreateCell(2).SetCellValue(x.TypeName);
                r.CreateCell(3).SetCellValue(x.Brand);
                r.CreateCell(4).SetCellValue(x.Model);
                r.CreateCell(5).SetCellValue(x.SerialNo);
                r.CreateCell(6).SetCellValue(x.PurchaseDate?.ToString("yyyy-MM-dd"));
                r.CreateCell(7).SetCellValue(AssetStatusText(x.Status));
                r.CreateCell(8).SetCellValue(x.OwnerName);
                r.CreateCell(9).SetCellValue(x.Dept);
                r.CreateCell(10).SetCellValue(x.Location);
                r.CreateCell(11).SetCellValue(x.Remark);
                r.CreateCell(12).SetCellValue(x.CreatedAt.ToString("yyyy-MM-dd HH:mm"));
            }

            for (int i = 0; i < cols.Length; i++) sheet.AutoSizeColumn(i);

            using (var ms = new MemoryStream())
            {
                wb.Write(ms);
                AuditService.Add(db, "Asset", null, "EXPORT", me, "资产导出（按筛选）");
                db.SaveChanges();
                return File(ms.ToArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "Assets_Export.xlsx");
            }
        }

        // 导入 Excel
        [HttpPost]
        public ActionResult Import()
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" });

            if (!IsAdmin(me.Id)) return Json(new { code = 1, msg = "无权限（仅管理员）" });

            if (Request.Files == null || Request.Files.Count == 0) return Json(new { code = 1, msg = "请选择文件" });

            var file = Request.Files[0];
            if (file == null || file.ContentLength <= 0) return Json(new { code = 1, msg = "文件为空" });

            var ext = Path.GetExtension(file.FileName)?.ToLower();
            if (ext != ".xlsx") return Json(new { code = 1, msg = "仅支持 .xlsx" });

            // 读取 Excel
            List<string> errors = new List<string>();
            int success = 0;

            using (var ms = new MemoryStream())
            {
                file.InputStream.CopyTo(ms);
                ms.Position = 0;

                IWorkbook wb = new XSSFWorkbook(ms);
                var sheet = wb.GetSheetAt(0);
                if (sheet == null) return Json(new { code = 1, msg = "Excel 无内容" });

                using (var tx = db.Database.BeginTransaction())
                {
                    try
                    {
                        // 预加载类型字典：Name -> Id
                        var typeDict = db.AssetTypes.Where(t => t.Status == 1).ToList()
                            .ToDictionary(t => t.Name, t => t.Id);

                        for (int i = 1; i <= sheet.LastRowNum; i++) // 从第2行开始
                        {
                            var row = sheet.GetRow(i);
                            if (row == null) continue;

                            string assetNo = GetCellString(row.GetCell(0));
                            string name = GetCellString(row.GetCell(1));
                            string typeName = GetCellString(row.GetCell(2));

                            // 空行跳过
                            if (string.IsNullOrWhiteSpace(assetNo) && string.IsNullOrWhiteSpace(name)) continue;

                            assetNo = (assetNo ?? "").Trim();
                            name = (name ?? "").Trim();

                            if (string.IsNullOrWhiteSpace(assetNo))
                            {
                                errors.Add($"第{i + 1}行：AssetNo 不能为空");
                                continue;
                            }
                            if (string.IsNullOrWhiteSpace(name))
                            {
                                errors.Add($"第{i + 1}行：Name 不能为空");
                                continue;
                            }

                            // 唯一性校验
                            if (db.Assets.Any(x => x.IsDeleted == false && x.AssetNo == assetNo))
                            {
                                errors.Add($"第{i + 1}行：资产编号已存在：{assetNo}");
                                continue;
                            }

                            long? typeId = null;
                            if (!string.IsNullOrWhiteSpace(typeName))
                            {
                                if (typeDict.ContainsKey(typeName)) typeId = typeDict[typeName];
                                else
                                {
                                    errors.Add($"第{i + 1}行：类型不存在：{typeName}");
                                    continue;
                                }
                            }

                            var now = DateTime.Now;

                            var a = new Assets
                            {
                                AssetNo = assetNo,
                                Name = name,
                                TypeId = typeId,
                                Brand = GetCellString(row.GetCell(3)),
                                Model = GetCellString(row.GetCell(4)),
                                SerialNo = GetCellString(row.GetCell(5)),
                                PurchaseDate = ParseDate(GetCellString(row.GetCell(6))),
                                Dept = GetCellString(row.GetCell(7)),
                                Location = GetCellString(row.GetCell(8)),
                                Remark = GetCellString(row.GetCell(9)),

                                Status = (byte)1,
                                OwnerUserId = null,
                                CreatedAt = now,
                                UpdatedAt = now,
                                IsDeleted = false
                            };

                            db.Assets.Add(a);
                            db.SaveChanges();

                            AddOpLog(a.Id, 1, null, a.Status, null, null, me.Id, "批量入库");
                            success++;
                        }

                        if (errors.Count > 0)
                        {
                            tx.Rollback();
                            return Json(new { code = 1, msg = "导入失败（请修正后重试）", data = new { errors } });
                        }
                        AuditService.Add(db, "Asset", null, "IMPORT", me,
    "资产Excel导入成功；新增：" + success + " 条；文件：" + file.FileName);

                        db.SaveChanges();
                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        tx.Rollback();
                        return Json(new { code = 1, msg = "导入异常：" + ex.Message });
                    }
                }
            }

            return Json(new { code = 0, msg = "导入成功", data = new { success } });
        }

        private string GetCellString(ICell cell)
        {
            if (cell == null) return null;
            cell.SetCellType(CellType.String);
            return cell.StringCellValue;
        }

        private DateTime? ParseDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            DateTime dt;
            if (DateTime.TryParse(s, out dt)) return dt.Date;
            return null;
        }

        // 资产操作记录页
        public ActionResult OpLog()
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return RedirectToAction("LoginIndex", "Login");

            // 可选：只有管理员可看全部；非管理员只看自己名下资产的操作记录
            ViewBag.IsAdmin = IsAdmin(me.Id);
            return View();
        }

        [HttpGet]
        public ActionResult OpLogListJson(int page = 1, int limit = 20,
    string keyword = null,        // 资产编号/名称
    byte? opType = null,
    long? operatorId = null,
    DateTime? from = null,
    DateTime? to = null)
        {
            var me = Session["LoginUser"] as Users;
            if (me == null) return Json(new { code = 1, msg = "未登录" }, JsonRequestBehavior.AllowGet);

            bool isAdmin = IsAdmin(me.Id);

            var q = from l in db.AssetOpLogs
                    join a in db.Assets on l.AssetId equals a.Id
                    join op0 in db.Users on l.OperatorId equals op0.Id into op1
                    from op in op1.DefaultIfEmpty()
                    where a.IsDeleted == false
                    select new
                    {
                        l.Id,
                        l.AssetId,
                        AssetNo = a.AssetNo,
                        AssetName = a.Name,
                        l.OpType,
                        l.FromStatus,
                        l.ToStatus,
                        l.FromOwnerId,
                        l.ToOwnerId,
                        OperatorId = l.OperatorId,
                        OperatorName = (op == null ? "" : op.RealName),
                        l.Remark,
                        l.CreatedAt,
                        OwnerUserId = a.OwnerUserId
                    };

            // 数据权限：非 admin 只看自己名下资产
            if (!isAdmin)
            {
                long myId = me.Id;
                q = q.Where(x => x.OwnerUserId == myId);
            }

            if (!string.IsNullOrWhiteSpace(keyword))
                q = q.Where(x => x.AssetNo.Contains(keyword) || x.AssetName.Contains(keyword));

            if (opType.HasValue)
                q = q.Where(x => x.OpType == opType.Value);

            if (operatorId.HasValue)
                q = q.Where(x => x.OperatorId == operatorId.Value);

            if (from.HasValue) q = q.Where(x => x.CreatedAt >= from.Value);
            if (to.HasValue) q = q.Where(x => x.CreatedAt <= to.Value);

            var total = q.Count();

            var list = q.OrderByDescending(x => x.CreatedAt)
                .Skip((page - 1) * limit)
                .Take(limit)
                .ToList();

            return Json(new { code = 0, msg = "", count = total, data = list }, JsonRequestBehavior.AllowGet);
        }

        private string AssetStatusText(byte v)
        {
            switch (v)
            {
                case 1: return "在库";
                case 2: return "使用中";
                case 3: return "维修中";
                case 4: return "已报废";
                default: return v.ToString();
            }
        }
        #endregion
    }
}