using System;
using System.Drawing;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper
{
    public sealed class Settings
    {
        private static Settings _instance;
        private GH_SettingsServer _server;
        private bool _showSolids;
        private bool _showAttributes;
        /*
        private Dictionary<Type, bool> _axisVisibility;
        private Dictionary<Type, double> _axisScale;
        private Dictionary<Type, bool> _attributeVisibility;
        private Dictionary<Type, double> _attributeScale;
        private Dictionary<Type, Color> _attributeColor;
        private double _tolerance;
        */

        public static Settings Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new Settings();
                return _instance;
            }
        }

        public double Tolerance => global::Rhino.RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;

        public bool ShowSolids
        {
            get => _showSolids;
            set
            {
                if (_showSolids != value)
                {
                    _showSolids = value;
                    _server.SetValue("ShowSolids", _showSolids);
                }
            }
        }

        public bool ShowAttributes
        {
            get => _showAttributes;
            set
            {
                if (_showAttributes != value)
                {
                    _showAttributes = value;
                    _server.SetValue("ShowAttributes", _showAttributes);
                }
            }
        }
        /*
        public double Tolerance
        {
            get => _tolerance;
            set
            {
                if (_tolerance != value)
                {
                    _tolerance = value;
                    _server.SetValue("Tolerance", _tolerance);
                }
            }
        }
        
        public bool IsEntityAxisVisible(Type type)
        {
            return _axisVisibility[type];
        }

        public void SetEntityAxisVisibility(Type type, bool isVisible)
        {
            if (_axisVisibility[type] != isVisible)
            {
                _axisVisibility[type] = isVisible;
                _server.SetValue($"{type.Name}_IsAxisVisible", isVisible);
            }
        }

        public double GetEntityAxisScale(Type type)
        {
            return _axisScale[type];
        }

        public void SetEntityAxisScale(Type type, double scale)
        {
            if (_axisScale[type] != scale)
            {
                _axisScale[type] = scale;
                _server.SetValue($"{type.Name}_AxisScale", scale);
            }
        }

        public bool HaveVisibilityOption(Type type)
        {
            return _attributeVisibility.ContainsKey(type);
        }

        public bool IsAttributeVisibile(Type type)
        {
            return _attributeVisibility[type];
        }

        public void SetAttributeVisibile(Type type, bool isVisible)
        {
            if (_attributeVisibility[type] != isVisible)
            {
                _attributeVisibility[type] = isVisible;
                _server.SetValue($"{type.Name}_IsVisible", isVisible);
            }
        }

        public bool HaveAttributeScaleOption(Type type)
        {
            return _attributeScale.ContainsKey(type);
        }

        public double GetAttributeScale(Type type)
        {
            return _attributeScale[type];
        }

        public void SetAttributeScale(Type type, double scale)
        {
            if (_attributeScale[type] != scale)
            {
                _attributeScale[type] = scale;
                _server.SetValue($"{type.Name}_Scale", scale);
            }
        }

        public bool HaveAttributeColor(Type type)
        {
            return _attributeColor.ContainsKey(type);
        }

        public Color GetAttributeColor(Type type)
        {
            return _attributeColor[type];
        }

        public void SetAttributeColor(Type type, Color color)
        {
            if (_attributeColor[type] != color)
            {
                _attributeColor[type] = color;
                _server.SetValue($"{type.Name}_Color", color);
            }
        }
        */
        private Settings()
        {
            _server = new GH_SettingsServer("BridGHe", true);
            _showSolids = _server.GetValue("ShowSolids", true);
            _showAttributes = _server.GetValue("ShowAttributes", true);
            /*
            _tolerance = _server.GetValue("Tolerance", 0.001);

            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.FullName.StartsWith("BridGHe.Common")).First();

            _axisVisibility = new Dictionary<Type, bool>();
            _axisScale = new Dictionary<Type, double>();

            IEnumerable<Type> entityTypes = assembly.GetTypes()
                .Where(type => !type.IsAbstract && type.IsSubclassOf(typeof(Common.Models.ElementModel)));
            foreach (Type type in entityTypes)
            {
                AddEntitySetting(type);
            }

            _attributeVisibility = new Dictionary<Type, bool>();
            _attributeScale = new Dictionary<Type, double>();
            _attributeColor = new Dictionary<Type, Color>();

            IEnumerable<Type> attributeTypes = assembly.GetTypes()
                .Where(type => !type.IsAbstract && type.IsSubclassOf(typeof(Common.Models.AttributeModel)));
            foreach (Type type in attributeTypes)
            {
                AddAttributeSetting(type);
            }

            IEnumerable<Type> loadTypes = assembly.GetTypes()
                .Where(type => !type.IsAbstract && type.IsSubclassOf(typeof(Common.Models.LoadModel)));
            foreach (Type type in loadTypes)
            {
                AddAttributeSetting(type);
            }*/
        }
        /*
        private void AddEntitySetting(Type type)
        {
            PropertyInfo pi = type.GetProperty("IsAxisVisible");
            if (pi != null)
            {
                bool isVisible = _server.GetValue($"{type.Name}_IsAxisVisible", Convert.ToBoolean(pi.GetValue(null)));
                _axisVisibility.Add(type, isVisible);
                pi.SetValue(null, isVisible);
            }

            pi = type.GetProperty("AxisScale");
            if (pi != null)
            {
                double scale = _server.GetValue($"{type.Name}_AxisScale", Convert.ToDouble(pi.GetValue(null)));
                _axisScale.Add(type, scale);
                pi.SetValue(null, scale);
            }
        }

        private void AddAttributeSetting(Type type)
        {
            MethodInfo mi = type.GetMethod("Draw");
            if (mi.DeclaringType == typeof(Common.Models.AttributeModel))
                return;

            PropertyInfo pi = type.GetProperty("IsVisible");
            if (pi != null)
            {
                bool isVisible = _server.GetValue($"{type.Name}_IsVisible", Convert.ToBoolean(pi.GetValue(null)));
                _attributeVisibility.Add(type, isVisible);
                pi.SetValue(null, isVisible);
            }
            pi = type.GetProperty("Scale");
            if (pi != null)
            {
                double scale = _server.GetValue($"{type.Name}_Scale", Convert.ToDouble(pi.GetValue(null)));
                _attributeScale.Add(type, scale);
                pi.SetValue(null, scale);
            }
            pi = type.GetProperty("Color");
            if (pi != null)
            {
                Color color = _server.GetValue($"{type.Name}_Color", (Color)pi.GetValue(null));
                _attributeColor.Add(type, color);
                pi.SetValue(null, color);
            }
        }
        */
        public void Save()
        {
            _server.WritePersistentSettings();
        }
    }
}
