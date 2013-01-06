using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using System.Configuration;

namespace Liechty.Alfalfa.Test
{
    [TestClass]
    public class GeoTests
    {
        private static readonly string AzureStorageCredentialsAccount = ConfigurationManager.AppSettings["AzureStorageCredentialsAccount"];
        private static readonly string AzureStorageCredentialsKey = ConfigurationManager.AppSettings["AzureStorageCredentialsKey"];
        private const string GeoTestsTable = "GeoTests";

        [TestMethod]
        public void BinaryToLatLong()
        {
            GeoLocation geo = new GeoLocation(0xF000000000000000);
            Assert.AreEqual(45.0, geo.Latitude);
            Assert.AreEqual(90.0, geo.Longitude);
        }

        [TestMethod]
        public void LatLongToBinary()
        {
            GeoLocation geo = new GeoLocation(45, 90);
            Assert.AreEqual(0xF000000000000000, geo.Code);
        }

        [TestMethod]
        public void RadiansFrom180()
        {
            GeoLocation geo1 = new GeoLocation(45, 90);
            GeoLocation geo2 = new GeoLocation(-45, -90);
            Assert.AreEqual(Math.PI, geo1.RadiansFrom(geo2));
        }

        [TestMethod]
        public void RadiansFrom90()
        {
            GeoLocation geo1 = new GeoLocation(45, 90);
            GeoLocation geo2 = new GeoLocation(0, 0);
            Assert.IsTrue(geo1.RadiansFrom(geo2) - Math.PI / 2.0 < 1E-15);
        }

        [TestMethod]
        public void GetItemsWithinTenKilometers()
        {
            GeoStore<GeoItem, GeoItemEntity> store = GetGeoStore();
            var home = GeoItem.Create(new GeoLocation(37.756235, -122.47727));
            var g1 = GeoItem.Create(new GeoLocation(37.756240, -122.47727));
            var g2 = GeoItem.Create(new GeoLocation(37.756235, -122.47730));
            var g3 = GeoItem.Create(new GeoLocation(37.756235, -121));
            var g4 = GeoItem.Create(new GeoLocation(37.756240, -122.47730));

            store.Upsert(g1, g2, g3, g4);
            store.SaveChanges();

            List<GeoItem> nearby;
            try
            {
                nearby = store.GetItemsNear(home.GeoLocation.Latitude, home.GeoLocation.Longitude, 10000).ToList();
            }
            finally
            {
                store.Delete(g1, g2, g3, g4);
                store.SaveChanges();
            }

            Assert.AreEqual(3, nearby.Count);
            Assert.AreEqual(1, nearby.Where(g => g.GeoLocation.Equals(g1.GeoLocation)).Count());
            Assert.AreEqual(1, nearby.Where(g => g.GeoLocation.Equals(g2.GeoLocation)).Count());
            Assert.AreEqual(0, nearby.Where(g => g.GeoLocation.Equals(g3.GeoLocation)).Count());
            Assert.AreEqual(1, nearby.Where(g => g.GeoLocation.Equals(g4.GeoLocation)).Count());
        }

        private static GeoStore<GeoItem, GeoItemEntity> GetGeoStore()
        {
            bool UseTableStorage = true;
            if (UseTableStorage)
            {
                StorageCredentialsAccountAndKey creds = new StorageCredentialsAccountAndKey(AzureStorageCredentialsAccount, AzureStorageCredentialsKey);
                
                CloudStorageAccount account = new CloudStorageAccount(creds, true);
                var client = account.CreateCloudTableClient();
                client.CreateTableIfNotExist(GeoTestsTable);
                TableServiceContext tableContext = client.GetDataServiceContext();

                var store = new GeoStore<GeoItem, GeoItemEntity>(new AzureTableGeoStoreSource<GeoItemEntity>(tableContext, GeoTestsTable));

                return store;
            }
            else
            {
                throw new NotImplementedException();
            }
        }
    }
}
