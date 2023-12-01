using AutoMapper;
using DinkToPdf;
using DinkToPdf.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemaVenta.AplicacionWeb.Models.ViewModels;
using SistemaVenta.AplicacionWeb.Utilidades.Response;
using SistemaVenta.BLL.Interfaces;
using SistemaVenta.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Security.Claims;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using System.Xml.Serialization;
using System.ServiceModel;
using System.ServiceModel.Channels;
using WSTimbrado;
using static SistemaVenta.AplicacionWeb.Models.ViewModels.VMXML;
using static System.Net.WebRequestMethods;
using static WSTimbrado.TimbradoSoapClient;

namespace SistemaVenta.AplicacionWeb.Controllers
{
    [Authorize]
    public class VentaController : Controller
    {
        private readonly ITipoDocumentoVentaService _tipoDocumentoVentaServicio;
        private readonly IVentaService _ventaServicio;
        private readonly IMapper _mapper;
        private readonly IConverter _converter;
        private readonly IServiceConsumer _serviceConsumer;
        private readonly IConfiguration _configuration;

        public VentaController(ITipoDocumentoVentaService tipoDocumentoVentaServicio,
            IVentaService ventaServicio,
            IMapper mapper,
            IConverter converter,
            IConfiguration configuration
        )
        {
            _tipoDocumentoVentaServicio = tipoDocumentoVentaServicio;
            _ventaServicio = ventaServicio;
            _mapper = mapper;
            _converter = converter;
            _configuration = configuration;
        }

        public IActionResult NuevaVenta()
        {
            return View();
        }

        public IActionResult HistorialVenta()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> ListaTipoDocumentoVenta()
        {
            List<VMTipoDocumentoVenta> vmListaTipoDocumentos = _mapper.Map<List<VMTipoDocumentoVenta>>(await _tipoDocumentoVentaServicio.Lista());
            return StatusCode(StatusCodes.Status200OK, vmListaTipoDocumentos);
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerProductos(string busqueda)
        {
            List<VMProducto> vmListaProductos = _mapper.Map<List<VMProducto>>(await _ventaServicio.ObtenerProductos(busqueda));
            return StatusCode(StatusCodes.Status200OK, vmListaProductos);
        }

        [HttpPost]
        public async Task<IActionResult> RegistrarVenta([FromBody] VMVenta modelo)
        {
            GenericResponse<VMVenta> gResponse = new GenericResponse<VMVenta>();
            try
            {
                ClaimsPrincipal claimUser = HttpContext.User;
                string idUsuario = claimUser.Claims
                    .Where(c => c.Type == ClaimTypes.NameIdentifier)
                    .Select(c => c.Value).SingleOrDefault();
                modelo.IdUsuario = int.Parse(idUsuario);

                // Realizar la venta
                Venta venta_creada = await _ventaServicio.Registrar(_mapper.Map<Venta>(modelo));
                modelo = _mapper.Map<VMVenta>(venta_creada);
                gResponse.Estado = true;
                gResponse.Objeto = modelo;
            }
            catch (Exception ex)
            {
                gResponse.Estado = false;
                gResponse.Mensaje = ex.Message;
            }
            try
            {
                // Genera PDF y XML
                GenerarFacturaPDF(modelo);
                GenerarFacturaXML(modelo);
                WebServiceLlamado(modelo.NumeroVenta);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            return StatusCode(StatusCodes.Status200OK, gResponse);
        }

        public void GenerarFacturaPDF(VMVenta venta)
        {
            // Lógica para generar el PDF
            var pdfPath = $"D:\\Facturas\\{venta.NumeroVenta}.pdf";
            var htmlContent = ObtenerContenidoHTMLFactura(venta); // Ajusta según tu necesidad

            var pdf = new HtmlToPdfDocument()
            {
                GlobalSettings = new GlobalSettings()
                {
                    PaperSize = PaperKind.A4,
                    Orientation = Orientation.Portrait,
                },
                Objects = {
                new ObjectSettings(){
                    HtmlContent = htmlContent
                }
            }
            };
            var archivoPDF = _converter.Convert(pdf);
            System.IO.File.WriteAllBytes(pdfPath, archivoPDF);
        }

        public void GenerarFacturaXML(VMVenta venta)
        {
            var xmlPath = $"C:\\Facturas\\{venta.NumeroVenta}_factura.xml";
            var xmlContent = ObtenerContenidoXMLFactura(venta);
            System.IO.File.WriteAllText(xmlPath, xmlContent);
        }

        private string ObtenerContenidoXMLFactura(VMVenta venta)
        {
            var producto = new Producto();
            foreach (var detalleVenta in venta.DetalleVenta)
            {
                int idProducto = (int)detalleVenta.IdProducto;
                producto = ObtenerDatosProducto(idProducto);
            }

            var cliente = ObtenerDatosCliente(venta.NombreCliente);

            var facturaData = new FacturaData
            {
                NumeroVenta = venta.NumeroVenta,
                Total = venta.Total,
                Empresa = new Empresa
                {
                    Nombre = "Ruben",
                    Direccion = "Monterrey",
                    ClaveContribuyente = "10923"
                },
                Producto = new ProductoInfo
                {
                    Nombre = producto.Marca,
                    Precio = producto.Precio
                },
                Cliente = new ClienteInfo
                {
                    Nombre = cliente.Nombre,
                    Domicilio = cliente.Domicilio,
                    RFC = cliente.RFC,
                    FechaExpedicion = cliente.FechaExpedicion.ToString("yyyy-MM-dd")
                }
            };

            venta.FacturaData = facturaData; 

            var xmlSerializer = new System.Xml.Serialization.XmlSerializer(typeof(FacturaData));
            using (var stringWriter = new StringWriter(new StringBuilder(), CultureInfo.InvariantCulture))
            {
                xmlSerializer.Serialize(stringWriter, facturaData);
                return stringWriter.ToString();
            }
        }

        public string ObtenerContenidoHTMLFactura(VMVenta venta)
        {
            string contenidoHTML = null;
            var producto = new Producto
            {
                IdProducto = 0,
                Marca = "",
                Precio = 0
            };
            foreach (var detalleVenta in venta.DetalleVenta)
            {
                int idProducto = (int)detalleVenta.IdProducto;
                producto = ObtenerDatosProducto(idProducto);
            }
            var cliente = ObtenerDatosCliente(venta.NombreCliente);
            
            string plantillaHTML = @"
                                <!DOCTYPE html>
                <html lang=""es"">
                <head>
                    <meta charset=""UTF-8"">
                    <title>Factura - 987654</title>
                </head>
                <body style=""font-family: 'Arial', sans-serif; margin: 20px; background-color: #f4f4f4;"">

                    <div style=""max-width: 600px; margin: 0 auto; background-color: #fff; padding: 20px; border-radius: 8px; box-shadow: 0 0 10px rgba(0, 0, 0, 0.1);"">

                        <h1 style=""color: #333; text-align: center;"">Factura</h1>
        
                        <div style=""margin-bottom: 20px;"">
                            <p>Número de Venta: {0}</p>
                            <p>Total: {1:C}</p>
                        </div>

                        <div style=""border: 1px solid #ddd; padding: 10px; margin-bottom: 15px; border-radius: 8px;"">
                            <h2>Información de la Empresa</h2>
                            <p>Nombre: ABC Solutions</p>
                            <p>Dirección: Calle Principal, Ciudad</p>
                            <p>Clave de Contribuyente: 456XYZ</p> 
                        </div>

                        <div style=""border: 1px solid #ddd; padding: 10px; margin-bottom: 15px; border-radius: 8px;"">
                            <h2>Detalles del Producto</h2>
                            <table style=""width: 100%; border-collapse: collapse; margin-top: 10px; border: 1px solid #ddd;"">
                                <tr>
                                    <th style=""padding: 8px; text-align: left; border: 1px solid #ddd; background-color: #f2f2f2;"">Nombre</th>
                                    <th style=""padding: 8px; text-align: left; border: 1px solid #ddd; background-color: #f2f2f2;"">Precio</th>
                                </tr>
                                <tr>
                                    <td style=""padding: 8px; text-align: left; border: 1px solid #ddd;"">{2}</td>
                                    <td style=""padding: 8px; text-align: left; border: 1px solid #ddd;"">{3:C}</td> 
                                </tr>
                            </table>
                        </div>

                        <div style=""border: 1px solid #ddd; padding: 10px; margin-bottom: 15px; border-radius: 8px;"">
                            <h2>Información del Cliente</h2>
                            <p>Nombre: {4}</p>
                            <p>Residencia: {5}</p>
                            <p>RFC: {6}</p>
                            <p>Fecha de Emisión: {7}</p>
                        </div>
                    </div>

                </body>
                </html>
                ";

            // Formateo de la plantilla
            contenidoHTML = string.Format(plantillaHTML, venta.NumeroVenta, venta.Total, producto.Marca, producto.Precio, cliente.Nombre, cliente.Domicilio, cliente.RFC, cliente.FechaExpedicion);
            return contenidoHTML;
        }

        public Producto ObtenerDatosProducto(int idProducto)
        {
            var connectionString = _configuration.GetConnectionString("CadenaSQL");
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // Consulta SQL 
                var query = $"SELECT idProducto, marca, precio FROM Producto WHERE idProducto = {idProducto}";

                using (var comando = new SqlCommand(query, connection))
                {
                    using (var lector = comando.ExecuteReader())
                    {
                        if (lector.Read())
                        {
                            // Objeto producto
                            return new Producto
                            {
                                IdProducto = Convert.ToInt32(lector["idProducto"]),
                                Marca = lector["Marca"].ToString(),
                                Precio = Convert.ToDecimal(lector["Precio"])
                            };
                        }
                    }
                }
            }
            return null;
        }

        public Cliente ObtenerDatosCliente(string NombreCliente)
        {
            var connectionString = _configuration.GetConnectionString("CadenaSQL");
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var query = $"SELECT ClienteId, Nombre, Domicilio, RFC, FechaExpedicion FROM Cliente WHERE Nombre = '{NombreCliente}'";

                using (var comando = new SqlCommand(query, connection))
                {
                    using (var lector = comando.ExecuteReader())
                    {
                        if (lector.Read())
                        {
                            // Crear un objeto Cliente con los datos de la base de datos
                            return new Cliente
                            {
                                ClienteId = Convert.ToInt32(lector["ClienteId"]),
                                Nombre = lector["Nombre"].ToString(),
                                Domicilio = lector["Domicilio"].ToString(),
                                RFC = lector["RFC"].ToString(),
                                FechaExpedicion = DateTime.Parse(lector["FechaExpedicion"].ToString())
                            };
                        }
                    }
                }
            }

            return null;
        }

        [HttpGet]
        public async Task<IActionResult> Historial(string numeroVenta, string fechaInicio, string fechaFin)
        {
            List<VMVenta> vmHistorialVenta = _mapper.Map<List<VMVenta>>(await _ventaServicio.Historial(numeroVenta, fechaInicio, fechaFin));
            return StatusCode(StatusCodes.Status200OK, vmHistorialVenta);
        }

        public IActionResult MostrarPDFVenta(string numeroVenta)
        {
            string pdfPath = $"D:\\Facturas\\{numeroVenta}.pdf";

            if (System.IO.File.Exists(pdfPath))
            {
                var archivoPDF = System.IO.File.ReadAllBytes(pdfPath);
                return File(archivoPDF, "application/pdf");
            }

            return NotFound(); // Manejar si el archivo no existe
        }

        public void WebServiceLlamado(string numeroVenta)
        {
            IServiceConsumer serviceConsumer = new ServiceConsumer();
            string Url = $"D:\\Facturas\\{numeroVenta}_factura.xml";
            string xmlContent = serviceConsumer.ReadXmlContent(Url);
            WSTimbrado.TimbrarFRequest timbrarRequest = new WSTimbrado.TimbrarFRequest()
            {
                Body = new TimbrarFRequestBody()
                {
                    Usuario = "FIME",
                    Password = "s9%4ns7q#eGq",
                    StrXml = xmlContent
                }
            };

            EndpointConfiguration endpointConfiguration = new EndpointConfiguration();
            TimbradoSoapClient timbradoSoapClient = new TimbradoSoapClient(endpointConfiguration: endpointConfiguration);

            // Configuración del encabezado SOAPAction
            var soapActionHeader = new HttpRequestMessageProperty();
            soapActionHeader.Headers["SOAPAction"] = "\"http://adon.mx/TimbrarF\""; // Nota las comillas dobles

            // Configuración del endpoint
            using (new OperationContextScope(timbradoSoapClient.InnerChannel))
            {
                OperationContext.Current.OutgoingMessageProperties[HttpRequestMessageProperty.Name] = soapActionHeader;

                string direccionPrueba = "https://ws.urbansa.com/app/timbrado.asmx";
                Uri miUri = new Uri(direccionPrueba);

                timbradoSoapClient.Endpoint.Address = CreateEndpointAddress(miUri);
                var Respuesta = timbradoSoapClient.TimbrarFAsync(timbrarRequest).Result;

                // Verifica si la respuesta es exitosa y contiene datos.
                if (Respuesta != null)
                {
                    Console.WriteLine($"Longitud de Respuesta: {Respuesta}");
                    //// Puedes cambiar "application/zip" al tipo de contenido correcto para tu archivo.
                    //return File(Respuesta, "application/zip", $"{numeroVenta}_factura.zip");
                }
                else
                {
                    // Maneja el caso en el que la respuesta no es válida o no contiene datos.
                    Console.WriteLine("La respuesta del servicio no es válida o no contiene datos.");
                }
            }
        }

        public static System.ServiceModel.EndpointAddress CreateEndpointAddress(Uri endpoint)
        {
            var endpointIdentity = new DnsEndpointIdentity("localhost");
            var endpointAddress = new System.ServiceModel.EndpointAddress(endpoint, endpointIdentity);

            return endpointAddress;
        }
    }
}