using System;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure.StorageClient;

namespace Liechty.Alfalfa
{
    public class GeoStore<TGeoItem, TGeoItemEntity>
        where TGeoItem : GeoItem, IHasEntity<TGeoItemEntity>, new()
        where TGeoItemEntity : GeoItemEntity
    {
        private IGeoStoreSource<TGeoItemEntity> Source { get; set; }

        public GeoStore(IGeoStoreSource<TGeoItemEntity> source)
        {
            this.Source = source;
        }

        public void Upsert(params TGeoItem[] geoItems)
        {
            foreach (TGeoItem item in geoItems)
            {
                this.Source.Upsert(item.Entity);
            }
        }

        public void Delete(params TGeoItem[] geoItems)
        {
            foreach (TGeoItem item in geoItems)
            {
                this.Source.Delete(item.Entity);
            }
        }

        public void SaveChanges()
        {
            this.Source.SaveChanges();
        }

        public IEnumerable<TGeoItem> GetItemsNear(double latitude, double longitude, double maxMetersAway)
        {
            List<GeoQuad> quads = GetUnionOfPartitionsCoveringCircle(latitude, longitude, maxMetersAway);
            IEnumerable<MinMax<ulong>> codeRanges = quads.Select(q => new MinMax<ulong>() { Min = q.Code, Max = q.Code | ~q.Mask });

            IEnumerable<TGeoItemEntity> itemsFromStore = this.Source.GetItems(codeRanges);
            GeoLocation center = new GeoLocation(latitude, longitude);
            return itemsFromStore
                .Select(o => { var item = new TGeoItem(); item.Initialize(o); return item; } )
                .Where(o => o.GeoLocation.MetersFrom(center) < maxMetersAway);
        }

        private static List<GeoQuad> GetUnionOfPartitionsCoveringCircle(double latitude, double longitude, double maxMetersAway)
        {
            GeoLocation location = new GeoLocation(latitude, longitude);
            GeoCircle queryCircle = new GeoCircle(location, maxMetersAway);
            ulong code = location.Code;

            // Find top-level quad partition that encompasses the entire query region circle.
            bool encompassesCircle = false;
            ulong mask = ulong.MaxValue;
            GeoRect levelOneRect;
            do
            {
                mask = mask << 2;
                levelOneRect = GetGeoRect(code, mask);
                encompassesCircle = (levelOneRect.Southwest.Latitude <= queryCircle.MinLatitude && levelOneRect.Southwest.Longitude <= queryCircle.MinLongitude &&
                                     levelOneRect.Northeast.Latitude >= queryCircle.MaxLatitude && levelOneRect.Northeast.Longitude >= queryCircle.MaxLongitude);
            } while (!encompassesCircle);

            List<GeoQuad> finalQuads = new List<GeoQuad>();
            GeoQuad levelOneQuad = new GeoQuad(levelOneRect.Southwest.Code, mask);

            // Split the top-level quad partition into 4.
            List<GeoQuad> levelTwoQuads = new List<GeoQuad>(4);
            try
            {
                levelOneQuad.SplitAndAddToList(levelTwoQuads);
            }
            catch (InvalidOperationException)
            {
                // Can't split further.
                finalQuads.Add(levelOneQuad);
                return finalQuads;
            }

            PruneQuadsNotInQueryCircle(levelTwoQuads, 0, 4, queryCircle);

            // There will be 2, 3, or 4 level 2 quads, because if there were only 1, it would have
            // been the level 1 quad.  Divide these into level 3 quads and prune any that aren't in
            // the query circle.  If all quads at one split except one are pruned, split that
            // single quad again.  If no quads are pruned, just keep the original.
            List<GeoQuad> levelThreeQuads = new List<GeoQuad>(4 * 3);
            SplitQuadsAndPrune(levelTwoQuads, levelThreeQuads, finalQuads, queryCircle, 3);
            
            // For each level 2 quad, there will be 0, 2 or 3 level 3 quads, because if there were
            // only one, it would be split further, and if there were 4, the original would become
            // a final quad.
            SplitQuadsAndPrune(levelThreeQuads, finalQuads, finalQuads, queryCircle, 2);

            return finalQuads;
        }

        private static void SplitQuadsAndPrune(
            IEnumerable<GeoQuad> currentLevelQuads,
            List<GeoQuad> newLevelQuads,
            List<GeoQuad> finalQuads,
            GeoCircle queryCircle,
            int maxKeepableSplitQuads)
        {
            foreach (GeoQuad levelTwoQuad in currentLevelQuads)
            {
                GeoQuad quad = levelTwoQuad;
                int index = newLevelQuads.Count;
                // Split and prune until we end up with more than one surviving quad.
                do
                {
                    try
                    {
                        quad.SplitAndAddToList(newLevelQuads);
                    }
                    catch (InvalidOperationException)
                    {
                        // Can't split any further.
                        finalQuads.Add(quad);
                        continue;
                    }

                    PruneQuadsNotInQueryCircle(newLevelQuads, index, 4, queryCircle);
                    if (newLevelQuads.Count == index + 1)
                    {
                        quad = newLevelQuads[index];
                        newLevelQuads.RemoveAt(index);
                    }
                } while (newLevelQuads.Count == index + 1);

                // If not enough quads pruned, just keep the original and stop splitting.
                if (newLevelQuads.Count > index + maxKeepableSplitQuads)
                {
                    newLevelQuads.RemoveRange(index, newLevelQuads.Count - index);
                    finalQuads.Add(quad);
                }
            }
        }

        private static void PruneQuadsNotInQueryCircle(List<GeoQuad> quads, int minIndex, int count, GeoCircle queryCircle)
        {
            for (int i = minIndex; i < minIndex + count; ++i)
            {
                GeoQuad quad = quads[i];
                GeoRect levelTwoRect = GetGeoRect(quad.Code, quad.Mask);
                // Prune quads that aren't in the query circle.
                if (!levelTwoRect.OverlapsCircle(queryCircle))
                {
                    quads.RemoveAt(i--);
                    --count;
                }
            }
        }

        private struct GeoQuad
        {
            public GeoQuad(ulong code, ulong mask)
                : this()
            {
                this.Code = code;
                this.Mask = mask;
            }

            public ulong Code { get; set; }
            public ulong Mask { get; set; }

            private const ulong southWestPattern = 0x0000000000000000;  // 00 repeated
            private const ulong northWestPattern = 0x5555555555555555;  // 01 repeated
            private const ulong southEastPattern = 0xAAAAAAAAAAAAAAAA;  // 10 repeated
            private const ulong northEastPattern = 0xFFFFFFFFFFFFFFFF;  // 11 repeated
            public void SplitAndAddToList(List<GeoQuad> split)
            {
                ulong newMask = (this.Mask >> 2) | 0xC000000000000000;
                if (newMask > 0xFFFFFFFFFFFFFFFC)
                {
                    throw new InvalidOperationException("Cannot split the quadrant into smaller quadrants");
                }

                // 2 bits set just to the right of the current mask.
                ulong quadrantSplitMask = this.Mask ^ newMask;

                split.Add(new GeoQuad(this.Code | (quadrantSplitMask & northEastPattern), newMask));
                split.Add(new GeoQuad(this.Code | (quadrantSplitMask & northWestPattern), newMask));
                split.Add(new GeoQuad(this.Code | (quadrantSplitMask & southEastPattern), newMask));
                split.Add(new GeoQuad(this.Code | (quadrantSplitMask & southWestPattern), newMask));
            }

            public override string ToString()
            {
                return GetGeoRect(this.Code, this.Mask).ToString();
            }
        }

        private static GeoRect GetGeoRect(ulong code, ulong mask)
        {
            ulong minCode = code & mask;
            ulong maxCode = code | ~mask;

            GeoRect geoRect = new GeoRect()
            {
                Southwest = new GeoLocation(minCode),
                Northeast = new GeoLocation(maxCode)
            };

            return geoRect;
        }
    }

    public interface IGeoStoreSource<TGeoItemEntity> where TGeoItemEntity : GeoItemEntity
    {
        IList<TGeoItemEntity> GetItems(IEnumerable<MinMax<ulong>> codeRanges);
        void Upsert(GeoItemEntity entity);
        void Delete(GeoItemEntity entity);
        void SaveChanges();
    }

    public struct MinMax<T>
    {
        public T Min { get; set; }
        public T Max { get; set; }

        public override string ToString()
        {
            return String.Format("{0}..{1}", this.Min, this.Max);
        }
    }

    public class AzureTableGeoStoreSource<TGeoItemEntity> : IGeoStoreSource<TGeoItemEntity> where TGeoItemEntity : GeoItemEntity
    {
        public TableServiceContext TableContext { get; private set; }
        public string TableName { get; private set; }

        public AzureTableGeoStoreSource(TableServiceContext tableContext, string tableName)
        {
            this.TableContext = tableContext;
            this.TableName = tableName;
        }

        public IList<TGeoItemEntity> GetItems(IEnumerable<MinMax<ulong>> codeRanges)
        {
            List<MinMax<string>> ranges = codeRanges.Select(r => new MinMax<string>(){ Min = GeoItem.GetPaddedCodeString(r.Min), Max = GeoItem.GetPaddedCodeString(r.Max) }).ToList();
            var tableQ = this.TableContext.CreateQuery<TGeoItemEntity>(this.TableName);

            IQueryable<TGeoItemEntity> q;
            switch (ranges.Count)
            {
                case 1:
                    q = tableQ.Where(g => g.PartitionKey.CompareTo(ranges[0].Min) >= 0 && g.PartitionKey.CompareTo(ranges[0].Max) <= 0);
                    break;
                case 2:
                    q = tableQ.Where(g => g.PartitionKey.CompareTo(ranges[0].Min) >= 0 && g.PartitionKey.CompareTo(ranges[0].Max) <= 0 ||
                    g.PartitionKey.CompareTo(ranges[1].Min) >= 0 && g.PartitionKey.CompareTo(ranges[1].Max) <= 0);
                    break;
                case 3:
                    q = tableQ.Where(g => g.PartitionKey.CompareTo(ranges[0].Min) >= 0 && g.PartitionKey.CompareTo(ranges[0].Max) <= 0 ||
                    g.PartitionKey.CompareTo(ranges[1].Min) >= 0 && g.PartitionKey.CompareTo(ranges[1].Max) <= 0 ||
                    g.PartitionKey.CompareTo(ranges[2].Min) >= 0 && g.PartitionKey.CompareTo(ranges[2].Max) <= 0);
                    break;
                case 4:
                    q = tableQ.Where(g => g.PartitionKey.CompareTo(ranges[0].Min) >= 0 && g.PartitionKey.CompareTo(ranges[0].Max) <= 0 ||
                    g.PartitionKey.CompareTo(ranges[1].Min) >= 0 && g.PartitionKey.CompareTo(ranges[1].Max) <= 0 ||
                    g.PartitionKey.CompareTo(ranges[2].Min) >= 0 && g.PartitionKey.CompareTo(ranges[2].Max) <= 0 ||
                    g.PartitionKey.CompareTo(ranges[3].Min) >= 0 && g.PartitionKey.CompareTo(ranges[3].Max) <= 0);
                    break;
                default:
                    throw new NotImplementedException();
            }

            var results = q.ToList();
            return results; 
        }

        public void Upsert(GeoItemEntity entity)
        {
            this.TableContext.AttachTo(this.TableName, entity);
            this.TableContext.UpdateObject(entity);
        }

        public void Delete(GeoItemEntity entity)
        {
            this.TableContext.DeleteObject(entity);
        }

        public void SaveChanges()
        {
            this.TableContext.SaveChangesWithRetries(SaveChangesOptions.ReplaceOnUpdate);
        }
    }
}
