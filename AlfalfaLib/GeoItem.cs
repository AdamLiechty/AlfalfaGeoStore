using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure.StorageClient;

namespace Liechty.Alfalfa
{
    public interface IHasEntity<TEntity>
    {
        TEntity Entity { get; }
        void Initialize(TEntity entity);
    }

    public class GeoItem : IHasEntity<GeoItemEntity>
    {
        private const ulong DefaultPartitionMask = 0xFFFFFFFFFFFFFFFF;

        public GeoItemEntity Entity { get; private set; }

        public DateTime Created { get { return this.Entity.Created; } }

        public void Initialize(GeoItemEntity entity)
        {
            this.Entity = entity;
            this.geoLocation = null;
        }

        public static GeoItem Create(GeoLocation location)
        {
            GeoItem geoItem = new GeoItem();
            geoItem.Entity = new GeoItemEntity() { RowKey = new Guid().ToString() };
            geoItem.Entity.Created = DateTime.UtcNow;
            geoItem.GeoLocation = location;
            return geoItem;
        }

        private GeoLocation geoLocation;
        public GeoLocation GeoLocation
        {
            get
            {
                if (this.geoLocation == null)
                {
                    ulong geoCode;
                    if (!ulong.TryParse(this.Entity.PartitionKey, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out geoCode))
                    {
                        geoCode = 0;
                    }

                    this.geoLocation = new GeoLocation(geoCode);
                }

                return this.geoLocation;
            }

            set
            {
                this.geoLocation = value;
                this.Entity.PartitionKey = GetPaddedCodeString(value.Code); 
            }
        }
        
        public static string GetPaddedCodeString(ulong code)
        {
            return String.Format("{0:X16}", code);
        }
    }

    public class GeoItemEntity : TableServiceEntity
    {
        public DateTime Created { get; set; }
    }
}
