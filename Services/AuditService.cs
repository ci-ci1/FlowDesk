using FlowDesk.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace FlowDesk.Services
{
    public static class AuditService
    {
        /// <summary>
        /// 写审计日志（只 Add，不 SaveChanges；由外层事务/SaveChanges 控制提交）
        /// </summary>
        public static void Add(FlowDeskEntities db,
            string bizType, long? bizId, string action,
            Users me, string remark,
            string beforeJson = null, string afterJson = null)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (me == null) throw new ArgumentNullException(nameof(me));

            db.AuditLogs.Add(new AuditLogs
            {
                BizType = bizType,
                BizId = bizId,
                Action = action,
                OperatorId = me.Id,
                OperatorName = me.RealName + "(" + me.UserName + ")",
                Remark = remark,
                BeforeJson = beforeJson,
                AfterJson = afterJson,
                CreatedAt = DateTime.Now
            });
        }
    }
}