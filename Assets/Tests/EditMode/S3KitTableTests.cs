using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Tests
{
    // S3 武器組合總表:逐列對照 datamine(Docs/Research/Data/Params/Common/WeaponInfoMain.json)
    public class S3KitTableTests
    {
        [Serializable]
        private sealed class UiParam
        {
            public string Type;
            public int Value;
        }

        [Serializable]
        private sealed class MainRow
        {
            public string __RowId;
            public string Type;
            public string SubWeapon;
            public string SpecialWeapon;
            public int SpecialPoint;
            public UiParam[] UIParam;
        }

        [Serializable]
        private sealed class MainTable
        {
            public MainRow[] rows;
        }

        private static Dictionary<string, MainRow> _versus;

        private static Dictionary<string, MainRow> Versus()
        {
            if (_versus != null)
            {
                return _versus;
            }
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "Research", "Data", "Params",
                "Common", "WeaponInfoMain.json"));
            Assert.IsTrue(File.Exists(path), "找不到 " + path);
            MainTable table = JsonUtility.FromJson<MainTable>("{\"rows\":" + File.ReadAllText(path) + "}");
            _versus = new Dictionary<string, MainRow>();
            foreach (MainRow row in table.rows)
            {
                if (row.Type == "Versus")
                {
                    _versus[row.__RowId] = row;
                }
            }
            return _versus;
        }

        // Work/Gyml/Bomb_Suction.spl__WeaponInfoSub.gyml → Bomb_Suction
        private static string InternalName(string gymlPath)
        {
            string file = gymlPath.Substring(gymlPath.LastIndexOf('/') + 1);
            return file.Substring(0, file.IndexOf('.'));
        }

        [Test]
        public void 每列的副武器大招點數能力值與datamine一致()
        {
            Dictionary<string, MainRow> versus = Versus();
            foreach (S3KitTable.Row row in S3KitTable.Rows)
            {
                Assert.IsTrue(versus.TryGetValue(row.RowId, out MainRow data), $"{row.RowId} 不在 Versus 列");
                Assert.AreEqual(InternalName(data.SubWeapon), row.Sub, $"{row.RowId} 副武器");
                Assert.AreEqual(InternalName(data.SpecialWeapon), row.Special, $"{row.RowId} 大招");
                Assert.AreEqual(data.SpecialPoint, row.SpecialPoints, $"{row.RowId} 大招點數");
                Assert.AreEqual(3, data.UIParam.Length, $"{row.RowId} 能力條數");
                Assert.AreEqual(data.UIParam[0].Type, row.Stat1, $"{row.RowId} 能力 1");
                Assert.AreEqual(data.UIParam[0].Value, row.Value1, $"{row.RowId} 能力 1 值");
                Assert.AreEqual(data.UIParam[1].Type, row.Stat2, $"{row.RowId} 能力 2");
                Assert.AreEqual(data.UIParam[1].Value, row.Value2, $"{row.RowId} 能力 2 值");
                Assert.AreEqual(data.UIParam[2].Type, row.Stat3, $"{row.RowId} 能力 3");
                Assert.AreEqual(data.UIParam[2].Value, row.Value3, $"{row.RowId} 能力 3 值");
            }
        }

        [Test]
        public void 已收的主武器系列沒有漏掉任何Versus組合()
        {
            var families = new HashSet<string>();
            foreach (S3KitTable.Family family in S3KitTable.Families)
            {
                families.Add(family.Name);
            }
            var listed = new HashSet<string>();
            foreach (S3KitTable.Row row in S3KitTable.Rows)
            {
                listed.Add(row.RowId);
            }
            foreach (string rowId in Versus().Keys)
            {
                if (families.Contains(S3KitTable.FamilyOf(rowId)))
                {
                    Assert.IsTrue(listed.Contains(rowId), $"{rowId} 屬於已收的系列但不在總表");
                }
            }
        }

        [Test]
        public void 順序依S3武器Id且識別碼不重複()
        {
            var kitIds = new HashSet<string>();
            var rowIds = new HashSet<string>();
            string lastFamily = null;
            var seenFamilies = new HashSet<string>();
            foreach (S3KitTable.Row row in S3KitTable.Rows)
            {
                Assert.IsTrue(kitIds.Add(row.KitId), "組合 id 重複:" + row.KitId);
                Assert.IsTrue(rowIds.Add(row.RowId), "RowId 重複:" + row.RowId);
                Assert.IsFalse(string.IsNullOrEmpty(row.Name) || string.IsNullOrEmpty(row.English), row.RowId + " 缺名稱");
                // 同系列連續排列(S3 清單順序)
                if (row.Family != lastFamily)
                {
                    Assert.IsTrue(seenFamilies.Add(row.Family), row.Family + " 沒有連續排列");
                    lastFamily = row.Family;
                }
            }
        }

        [Test]
        public void 零件與系列都有對照()
        {
            foreach (S3KitTable.Row row in S3KitTable.Rows)
            {
                Assert.IsTrue(S3KitTable.TryGetFamily(row.Family, out S3KitTable.Family family), row.RowId + " 沒有系列");
                Assert.IsFalse(string.IsNullOrEmpty(family.MainId), row.Family + " 沒有主武器 id");
                Assert.IsTrue(family.Weight >= 0 && family.Weight <= 2, row.Family + " 重量");
                Assert.IsTrue(S3KitTable.TryGetSub(row.Sub, out _), row.RowId + " 副武器沒有對照:" + row.Sub);
                Assert.IsTrue(S3KitTable.TryGetSpecial(row.Special, out _), row.RowId + " 大招沒有對照:" + row.Special);
                Assert.AreNotEqual(row.Stat1, S3KitTable.StatLabel(row.Stat1), row.Stat1 + " 沒有中文名");
                Assert.AreNotEqual(row.Stat2, S3KitTable.StatLabel(row.Stat2), row.Stat2 + " 沒有中文名");
                Assert.AreNotEqual(row.Stat3, S3KitTable.StatLabel(row.Stat3), row.Stat3 + " 沒有中文名");
            }
        }

        [Test]
        public void 暫代零件取自同系列的組合()
        {
            foreach (S3KitTable.StandIn standIn in S3KitTable.StandIns)
            {
                Assert.IsTrue(S3KitTable.TryGetRow(standIn.RowId, out S3KitTable.Row row), standIn.RowId);
                Assert.IsTrue(S3KitTable.TryGetRow(standIn.SourceRowId, out S3KitTable.Row source), standIn.SourceRowId);
                Assert.AreEqual(row.Family, source.Family, "暫代來源要同系列");
                Assert.IsTrue(row.Sub == standIn.Missing || row.Special == standIn.Missing, standIn.RowId + " 沒有這個零件");
                Assert.IsTrue(source.Sub == standIn.Replacement || source.Special == standIn.Replacement,
                    standIn.SourceRowId + " 沒有頂替的零件");
            }
        }

        [Test]
        public void 說明含主武器說明與補充()
        {
            Assert.IsTrue(S3KitTable.TryGetRow("Shooter_Normal_H", out S3KitTable.Row replica));
            string text = S3KitTable.Description(replica);
            StringAssert.StartsWith("萬用射擊槍", text);
            StringAssert.Contains("複製品", text);
            Assert.IsTrue(S3KitTable.TryGetRow("Shooter_Normal_00", out S3KitTable.Row basic));
            Assert.AreEqual("萬用射擊槍:射程、威力、射速均衡,三發擊倒。", S3KitTable.Description(basic));
        }
    }
}
