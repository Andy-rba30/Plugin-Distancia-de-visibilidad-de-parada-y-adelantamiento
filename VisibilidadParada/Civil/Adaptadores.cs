using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using VisibilidadParada.Nucleo;
using CivAlignment = Autodesk.Civil.DatabaseServices.Alignment;
using CivProfile = Autodesk.Civil.DatabaseServices.Profile;
using CivSurface = Autodesk.Civil.DatabaseServices.Surface;
using CivTinSurface = Autodesk.Civil.DatabaseServices.TinSurface;
using CivGridSurface = Autodesk.Civil.DatabaseServices.GridSurface;
using CivProfilePVI = Autodesk.Civil.DatabaseServices.ProfilePVI;
using CivProfileEntity = Autodesk.Civil.DatabaseServices.ProfileEntity;

namespace VisibilidadParada.Civil
{
    internal class EjeCivil : IEje
    {
        private readonly CivAlignment _al;
        public EjeCivil(CivAlignment al) { _al = al; }

        public bool Ubicar(double progresiva, double desfase, out double x, out double y)
        {
            x = 0; y = 0;
            try
            {
                double e = 0, n = 0;
                _al.PointLocation(progresiva, desfase, ref e, ref n);
                x = e; y = n;
                return true;
            }
            catch { return false; }
        }
    }

    internal class RasanteCivil : IRasante
    {
        private readonly CivProfile _pr;
        public RasanteCivil(CivProfile pr) { _pr = pr; }
        public double CotaEn(double progresiva) => _pr.ElevationAt(progresiva);
        public double PendienteEn(double progresiva) => _pr.GradeAt(progresiva);
    }

    internal static class GeometriaPerfil
    {
        /// <summary>
        /// Lee los PVI del perfil (progresiva y cota) y asocia a cada uno la curva vertical
        /// (entidad no tangente) que lo contiene, con su longitud y tipo.
        /// </summary>
        public static List<PviDato> LeerPvis(CivProfile pr)
        {
            var curvas = new List<(double ini, double fin, string tipo)>();
            foreach (CivProfileEntity ent in pr.Entities)
            {
                string tipo = ent.EntityType.ToString();
                if (tipo.IndexOf("Tangent", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                curvas.Add((ent.StartStation, ent.EndStation, TipoTxt(tipo)));
            }

            var lista = new List<PviDato>();
            int n = 1;
            foreach (CivProfilePVI pvi in pr.PVIs)
            {
                var d = new PviDato { N = n++, Progresiva = pvi.Station, Cota = pvi.Elevation };
                foreach (var c in curvas)
                {
                    if (d.Progresiva > c.ini + 1e-4 && d.Progresiva < c.fin - 1e-4)
                    {
                        d.TieneCurva = true;
                        d.LProyecto = c.fin - c.ini;
                        d.TipoEntidad = c.tipo;
                        break;
                    }
                }
                lista.Add(d);
            }
            lista.Sort((a, b) => a.Progresiva.CompareTo(b.Progresiva));
            return lista;
        }

        private static string TipoTxt(string t)
        {
            switch (t)
            {
                case "SymmetricParabola":
                case "ParabolaSymmetric": return "Parábola simétrica";
                case "AsymmetricParabola":
                case "ParabolaAsymmetric": return "Parábola asimétrica";
                case "Circular": return "Circular";
                default: return t;
            }
        }
    }

    internal class SuperficieCivil : ISuperficie
    {
        private readonly CivSurface _s;
        private readonly bool _hayCaja;
        private readonly double _x0, _y0, _x1, _y1;

        public SuperficieCivil(CivSurface s)
        {
            _s = s;
            try
            {
                Extents3d ext = s.GeometricExtents;
                _x0 = ext.MinPoint.X; _y0 = ext.MinPoint.Y;
                _x1 = ext.MaxPoint.X; _y1 = ext.MaxPoint.Y;
                _hayCaja = true;
            }
            catch { _hayCaja = false; }
        }

        public double CotaEn(double x, double y)
        {
            // Descarte rápido: evita excepciones costosas fuera de la superficie
            if (_hayCaja && (x < _x0 || x > _x1 || y < _y0 || y > _y1)) return double.NaN;
            try
            {
                if (_s is CivTinSurface tin) return tin.FindElevationAtXY(x, y);
                if (_s is CivGridSurface grid) return grid.FindElevationAtXY(x, y);
                return double.NaN;
            }
            catch { return double.NaN; }
        }
    }
}
