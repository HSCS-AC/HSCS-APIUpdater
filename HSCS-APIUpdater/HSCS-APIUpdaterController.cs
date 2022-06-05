using Microsoft.AspNetCore.Mvc;

namespace HSCS_APIUpdater;

[ApiController]
public class HSCS_APIUpdaterController : ControllerBase {
    private readonly HSCS_APIUpdater _APIUpdater;
    
    public HSCS_APIUpdaterController(HSCS_APIUpdater APIUpdater) {
        _APIUpdater = APIUpdater;
    }
    
    [HttpGet("/api/v1/get_server_info")]
    public JsonResult GetServerInfo() {
        return _APIUpdater.GetServerInfo();
    }
    
    [HttpGet("/api/v1/ping")]
    public ActionResult Ping() {
        return Ok();
    }
}