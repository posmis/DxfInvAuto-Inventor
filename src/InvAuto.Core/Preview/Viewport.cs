using System;

namespace InvAuto.Core.Preview
{
    /// <summary>
    /// Видимая область чертежа: масштаб и центр. Пересчитывает координаты чертежа (мм, ось Y вверх)
    /// в экранные (пиксели, ось Y вниз) и обратно; масштаб колесом вокруг курсора и сдвиг мышью.
    /// Масштаб хранится относительно «вписать целиком», поэтому при изменении размера окна
    /// чертёж остаётся на месте.
    /// </summary>
    public sealed class Viewport
    {
        /// <summary>Пределы масштаба относительно «вписать целиком».</summary>
        public const double MinZoom = 0.2;
        public const double MaxZoom = 5000;

        private double _minX, _minY, _maxX, _maxY;

        public Viewport(double padding = 3)
        {
            Padding = padding;
        }

        /// <summary>Поле вокруг чертежа при «вписать целиком», px.</summary>
        public double Padding { get; }

        public double ViewWidth { get; private set; }
        public double ViewHeight { get; private set; }

        /// <summary>Масштаб относительно «вписать целиком» (1 — чертёж целиком в окне).</summary>
        public double Zoom { get; private set; } = 1;

        /// <summary>Точка чертежа в центре окна.</summary>
        public Point2 Center { get; private set; }

        /// <summary>Пикселей на миллиметр при текущем масштабе.</summary>
        public double Scale => FitScale * Zoom;

        /// <summary>Пикселей на миллиметр, когда чертёж вписан целиком.</summary>
        public double FitScale
        {
            get
            {
                double w = Math.Max(_maxX - _minX, 1e-9), h = Math.Max(_maxY - _minY, 1e-9);
                double s = Math.Min((ViewWidth - 2 * Padding) / w, (ViewHeight - 2 * Padding) / h);
                return s > 0 ? s : 1e-9;
            }
        }

        /// <summary>Новый чертёж: запоминаем габариты и вписываем целиком.</summary>
        public void SetBounds(double minX, double minY, double maxX, double maxY)
        {
            _minX = minX;
            _minY = minY;
            _maxX = maxX;
            _maxY = maxY;
            Fit();
        }

        /// <summary>Размер окна изменился: масштаб и центр сохраняются.</summary>
        public void SetViewSize(double width, double height)
        {
            ViewWidth = width;
            ViewHeight = height;
        }

        public void Fit()
        {
            Zoom = 1;
            Center = new Point2((_minX + _maxX) / 2, (_minY + _maxY) / 2);
        }

        public Point2 ToScreen(Point2 model) => new Point2(
            ViewWidth / 2 + (model.X - Center.X) * Scale,
            ViewHeight / 2 - (model.Y - Center.Y) * Scale);

        public Point2 ToModel(double screenX, double screenY) => new Point2(
            Center.X + (screenX - ViewWidth / 2) / Scale,
            Center.Y - (screenY - ViewHeight / 2) / Scale);

        /// <summary>Масштаб в factor раз так, чтобы точка чертежа под курсором осталась под курсором.</summary>
        public void ZoomAt(double screenX, double screenY, double factor)
        {
            var anchor = ToModel(screenX, screenY);
            Zoom = Math.Max(MinZoom, Math.Min(MaxZoom, Zoom * factor));
            Center = new Point2(
                anchor.X - (screenX - ViewWidth / 2) / Scale,
                anchor.Y + (screenY - ViewHeight / 2) / Scale);
        }

        /// <summary>Сдвиг чертежа вслед за мышью на (dx; dy) пикселей.</summary>
        public void Pan(double dx, double dy)
        {
            Center = new Point2(Center.X - dx / Scale, Center.Y + dy / Scale);
        }
    }
}
