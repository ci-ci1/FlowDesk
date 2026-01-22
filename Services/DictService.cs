using FlowDesk.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace FlowDesk.Services
{
    public static class DictService
    {
        /// <summary>
        /// 按字典类型 Code 获取启用的字典项（按 Sort/Id 排序）
        /// </summary>
        public static List<DictItems> GetItems(FlowDeskEntities db, string typeCode)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (string.IsNullOrWhiteSpace(typeCode)) return new List<DictItems>();

            var typeId = db.DictTypes
                .Where(t => t.IsDeleted == false && t.Status == 1 && t.Code == typeCode)
                .Select(t => t.Id)
                .FirstOrDefault();

            if (typeId == 0) return new List<DictItems>();

            return db.DictItems
                .Where(i => i.IsDeleted == false && i.Status == 1 && i.TypeId == typeId)
                .OrderBy(i => i.Sort)
                .ThenBy(i => i.Id)
                .ToList();
        }

        /// <summary>
        /// 获取某个字典项的 Label（不存在返回默认值）
        /// </summary>
        public static string GetLabel(FlowDeskEntities db, string typeCode, string value, string defaultLabel = "")
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (string.IsNullOrWhiteSpace(typeCode) || string.IsNullOrWhiteSpace(value)) return defaultLabel;

            var typeId = db.DictTypes
                .Where(t => t.IsDeleted == false && t.Status == 1 && t.Code == typeCode)
                .Select(t => t.Id)
                .FirstOrDefault();

            if (typeId == 0) return defaultLabel;

            var label = db.DictItems
                .Where(i => i.IsDeleted == false && i.Status == 1 && i.TypeId == typeId && i.Value == value)
                .Select(i => i.Label)
                .FirstOrDefault();

            return label ?? defaultLabel;
        }
    }
}