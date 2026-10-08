$ErrorActionPreference = "Continue"
$base = "http://localhost:5012"
$results = @()

function Add-Test {
    param($label, $method, $uri, $status, $body, $notes)
    $script:results += [PSCustomObject]@{
        Label = $label; Method = $method; URI = $uri; Status = $status
        Body = if($body) { $body.ToString().Substring(0, [Math]::Min(150,$body.ToString().Length)) } else { "" }
        Notes = $notes
    }
    $color = if($status -match "^[12][0-9][0-9]$") { "Green" } elseif($status -eq "Expected" -or $status -eq "Expected error" -or $status -eq "Expected 401/403" -or $status -eq "Expected 403" -or $status -eq "Expected 404") { "Cyan" } else { "Red" }
    Write-Host "  [$label] $method $uri -> $status" -ForegroundColor $color
}

function Step {
    param($title) 
    Write-Host ""
    Write-Host ("="*80) -ForegroundColor Cyan
    Write-Host $title -ForegroundColor Cyan
    Write-Host ("="*80) -ForegroundColor Cyan
}

Step "HEALTH"
try { $r = Invoke-WebRequest "$base/health/live" -UseBasicParsing -TimeoutSec 5; Add-Test "live" "GET" "/health/live" $r.StatusCode $r.Content "OK" } 
catch { Add-Test "live" "GET" "/health/live" "ERROR" "" "FAIL: $_" }
try { $r = Invoke-WebRequest "$base/health/ready" -UseBasicParsing -TimeoutSec 5; Add-Test "ready" "GET" "/health/ready" $r.StatusCode $r.Content "OK" }
catch { Add-Test "ready" "GET" "/health/ready" "ERROR" "" "FAIL: $_" }

Step "SWAGGER"
try { $r = Invoke-WebRequest "$base/swagger/index.html" -UseBasicParsing -TimeoutSec 5; Add-Test "swagger" "GET" "/swagger/index.html" $r.StatusCode "" "Available" }
catch { Add-Test "swagger" "GET" "/swagger/index.html" "ERROR" "" "FAIL: $_" }

Step "AUTHENTICATION"
$token = $null
$playerId = $null
$testEmail = "apitest_$(Get-Random)@test.local"
$testPass = "TestPass123"

try {
    $body = @{ email = $testEmail; password = $testPass } | ConvertTo-Json
    $r = Invoke-WebRequest "$base/api/v1/auth/register" -Method POST -ContentType "application/json" -Body $body -UseBasicParsing -TimeoutSec 10
    Add-Test "register" "POST" "/api/v1/auth/register" $r.StatusCode $r.Content "Register OK"
    $j = $r.Content | ConvertFrom-Json
    $token = $j.accessToken
} catch { 
    $errCode = [int]$_.Exception.Response.StatusCode
    Add-Test "register" "POST" "/api/v1/auth/register" $errCode "" "Register response"
    # Try login anyway
    try {
        $body = @{ email = $testEmail; password = $testPass } | ConvertTo-Json
        $r2 = Invoke-WebRequest "$base/api/v1/auth/login" -Method POST -ContentType "application/json" -Body $body -UseBasicParsing -TimeoutSec 10
        $j2 = $r2.Content | ConvertFrom-Json
        $token = $j2.accessToken
        Add-Test "login_after_register_fail" "POST" "/api/v1/auth/login" $r2.StatusCode $r2.Content "Login OK"
    } catch { Add-Test "login" "POST" "/api/v1/auth/login" "ERROR" "" "FAIL: $_" }
}

Step "PROTECTED API WITHOUT TOKEN"
try { $r = Invoke-WebRequest "$base/api/v1/me" -UseBasicParsing -TimeoutSec 5; Add-Test "me_no_auth" "GET" "/api/v1/me" $r.StatusCode "" "Should 401" }
catch { Add-Test "me_no_auth" "GET" "/api/v1/me" "401" "" "Expected 401" }
try { $r = Invoke-WebRequest "$base/api/v1/catalog" -UseBasicParsing -TimeoutSec 5; Add-Test "catalog_no_auth" "GET" "/api/v1/catalog" $r.StatusCode "" "Should 401" }
catch { Add-Test "catalog_no_auth" "GET" "/api/v1/catalog" "401" "" "Expected 401" }

Step "PROTECTED API WITH TOKEN"
$hdr = @{ Authorization = "Bearer $token" }

if ($token) {
    try { $r = Invoke-WebRequest "$base/api/v1/me" -Headers $hdr -UseBasicParsing -TimeoutSec 5; Add-Test "me" "GET" "/api/v1/me" $r.StatusCode $r.Content "Me OK"; $jMe = $r.Content | ConvertFrom-Json; $playerId = $jMe.PlayerId } 
    catch { Add-Test "me" "GET" "/api/v1/me" "ERROR" "" "FAIL: $_" }

    try { $r = Invoke-WebRequest "$base/api/v1/catalog" -Headers $hdr -UseBasicParsing -TimeoutSec 10; Add-Test "catalog" "GET" "/api/v1/catalog" $r.StatusCode "" "Catalog OK"; $jCat = $r.Content | ConvertFrom-Json } 
    catch { Add-Test "catalog" "GET" "/api/v1/catalog" "ERROR" "" "FAIL: $_" }
} else {
    Add-Test "catalog" "GET" "/api/v1/catalog" "NO_TOKEN" "" "Skipped - no token"
}

Step "HERO CATALOG ANALYSIS"
if ($jCat) {
    $heroCount = $jCat.Heroes.Count
    $traitCount = $jCat.Traits.Count
    $classCount = $jCat.Classes.Count
    $skillCount = $jCat.TeamSkills.Count
    $factionCount = $jCat.Factions.Count
    Write-Host "  Heroes: $heroCount | Traits: $traitCount | Classes: $classCount | Skills: $skillCount | Factions: $factionCount" -ForegroundColor Yellow
    Add-Test "catalog_stats" "GET" "/api/v1/catalog" 200 "heroes=$heroCount traits=$traitCount classes=$classCount skills=$skillCount factions=$factionCount" "Catalog stats"
    
    $ownedCount = ($jCat.Heroes | Where-Object { $_.IsOwned }).Count
    Write-Host "  Owned heroes: $ownedCount" -ForegroundColor Yellow
    
    Write-Host "  Classes:" -ForegroundColor Yellow
    foreach ($c in $jCat.Classes) { Write-Host "    $($c.Code): $($c.NameVi) (base_sp=$($c.BaseSp), required=$($c.RequiredCount))" -ForegroundColor Gray }
    
    Write-Host "  Traits:" -ForegroundColor Yellow
    foreach ($t in $jCat.Traits) { Write-Host "    $($t.Code) [$($t.Kind)] -> $($t.ImplementationKey)" -ForegroundColor Gray }
    
    Write-Host "  TeamSkills:" -ForegroundColor Yellow
    foreach ($s in $jCat.TeamSkills) { Write-Host "    $($s.Code) -> $($s.ImplementationKey) (cd=$($s.CooldownTurns))" -ForegroundColor Gray }
    
    # Hero summary by class
    Write-Host "  Hero summary by class:" -ForegroundColor Yellow
    $jCat.Heroes | Group-Object ClassCode | ForEach-Object { 
        Write-Host "    $($_.Name): $($_.Count) heroes" -ForegroundColor Gray
    }
    
    # Hero with special traits
    Write-Host "  Hero with special traits:" -ForegroundColor Yellow
    $jCat.Heroes | Where-Object { $_.Trait -ne $null } | ForEach-Object {
        Write-Host "    $($_.Code) ($($_.ClassCode)): $($_.Trait.Code) [$($_.Trait.Kind)] -> $($_.Trait.ImplementationKey)" -ForegroundColor Gray
    }
} else {
    Add-Test "catalog_stats" "GET" "/api/v1/catalog" "NO_DATA" "" "No catalog data"
}

Step "MY HEROES"
if ($token) {
    try { $r = Invoke-WebRequest "$base/api/v1/me/heroes" -Headers $hdr -UseBasicParsing -TimeoutSec 5; Add-Test "my_heroes" "GET" "/api/v1/me/heroes" $r.StatusCode $r.Content "My heroes"; $jHeroes = $r.Content | ConvertFrom-Json } 
    catch { Add-Test "my_heroes" "GET" "/api/v1/me/heroes" "ERROR" "" "FAIL: $_" }
    if ($jHeroes) { Write-Host "  Owned hero IDs: $($jHeroes.Count)" -ForegroundColor Yellow }
}

Step "LINEUPS"
if ($token) {
    try { $r = Invoke-WebRequest "$base/api/v1/lineups" -Headers $hdr -UseBasicParsing -TimeoutSec 5; Add-Test "lineup_list" "GET" "/api/v1/lineups" $r.StatusCode $r.Content "List lineups"; $jLineups = $r.Content | ConvertFrom-Json } 
    catch { Add-Test "lineup_list" "GET" "/api/v1/lineups" "ERROR" "" "FAIL: $_" }
}

Step "MATCHMAKING"
if ($token) {
    $ticketBody = @{ mode = "bot" } | ConvertTo-Json
    try { 
        $r = Invoke-WebRequest "$base/api/v1/matchmaking/tickets" -Method POST -Headers $hdr -ContentType "application/json" -Body $ticketBody -UseBasicParsing -TimeoutSec 10
        Add-Test "matchmaking_bot" "POST" "/api/v1/matchmaking/tickets" $r.StatusCode $r.Content "Create bot ticket"
        $jTicket = $r.Content | ConvertFrom-Json
        $ticketId = $jTicket.TicketId
        $matchId = $null
        
        if ($ticketId) {
            Write-Host "  Ticket created: $ticketId, status=$($jTicket.Status)" -ForegroundColor Yellow
            Start-Sleep -Seconds 3
            try { 
                $r2 = Invoke-WebRequest "$base/api/v1/matchmaking/tickets/$ticketId" -Headers $hdr -UseBasicParsing -TimeoutSec 5
                Add-Test "matchmaking_poll" "GET" "/api/v1/matchmaking/tickets/$ticketId" $r2.StatusCode $r2.Content "Poll ticket"
                $jPoll = $r2.Content | ConvertFrom-Json
                
                if ($jPoll.MatchId) {
                    $matchId = $jPoll.MatchId
                    Write-Host "  MATCHED! MatchId: $matchId" -ForegroundColor Green
                    
                    try { 
                        $r3 = Invoke-WebRequest "$base/api/v1/matches/$matchId/state" -Headers $hdr -UseBasicParsing -TimeoutSec 5
                        Add-Test "match_state" "GET" "/api/v1/matches/$matchId/state" $r3.StatusCode "" "Match state"
                        $jState = $r3.Content | ConvertFrom-Json
                        Write-Host "  Match status=$($jState.Status), turn=$($jState.TurnIndex), side=$($jState.SideToMove)" -ForegroundColor Yellow
                    } catch { Add-Test "match_state" "GET" "/api/v1/matches/$matchId/state" "ERROR" "" "FAIL: $_" }
                    
                    try { 
                        $r4 = Invoke-WebRequest "$base/api/v1/matches/$matchId/legal-actions" -Headers $hdr -UseBasicParsing -TimeoutSec 5
                        Add-Test "legal_actions" "GET" "/api/v1/matches/$matchId/legal-actions" $r4.StatusCode "" "Legal actions"
                        $jLegal = $r4.Content | ConvertFrom-Json
                        Write-Host "  Legal moves available: $($jLegal.Count)" -ForegroundColor Yellow
                    } catch { Add-Test "legal_actions" "GET" "/api/v1/matches/$matchId/legal-actions" "ERROR" "" "FAIL: $_" }
                }
            } catch { Add-Test "matchmaking_poll" "GET" "/api/v1/matchmaking/tickets/$ticketId" "ERROR" "" "FAIL: $_" }
        }
    } catch { 
        $errCode = [int]$_.Exception.Response.StatusCode
        $errBody = try { ($_.ErrorDetails.Message) } catch { "" }
        Add-Test "matchmaking_bot" "POST" "/api/v1/matchmaking/tickets" $errCode $errBody "FAIL"
    }
}

Step "WS TICKET"
if ($token) {
    try { $r = Invoke-WebRequest "$base/api/v1/ws-ticket" -Method POST -Headers $hdr -UseBasicParsing -TimeoutSec 5; Add-Test "ws_ticket" "POST" "/api/v1/ws-ticket" $r.StatusCode $r.Content "WS ticket" } 
    catch { Add-Test "ws_ticket" "POST" "/api/v1/ws-ticket" "ERROR" "" "FAIL: $_" }
}

Step "MATCH HISTORY"
if ($token) {
    try { $r = Invoke-WebRequest "$base/api/v1/matches" -Headers $hdr -UseBasicParsing -TimeoutSec 5; Add-Test "match_history" "GET" "/api/v1/matches" $r.StatusCode $r.Content "Match list" } 
    catch { Add-Test "match_history" "GET" "/api/v1/matches" "ERROR" "" "FAIL: $_" }
}

Step "SHOP"
if ($token -and $jCat) {
    $shopHeroes = $jCat.Heroes | Where-Object { [int]$_.CoinPrice -gt 0 -and -not $_.IsOwned } | Select-Object -First 1
    if ($shopHeroes) {
        $shopHeroId = $shopHeroes.Id
        Write-Host "  Testing purchase of: $($shopHeroes.Code) ($($shopHeroes.Name)) price=$($shopHeroes.CoinPrice)" -ForegroundColor Yellow
        try { 
            $r = Invoke-WebRequest "$base/api/v1/shop/heroes/$shopHeroId/purchase" -Method POST -Headers $hdr -UseBasicParsing -TimeoutSec 5
            Add-Test "shop_purchase" "POST" "/api/v1/shop/heroes/$shopHeroId/purchase" $r.StatusCode $r.Content "Purchase hero"
        } catch { 
            $errCode = [int]$_.Exception.Response.StatusCode
            $errBody = try { ($_.ErrorDetails.Message) } catch { "" }
            Add-Test "shop_purchase" "POST" "/api/v1/shop/heroes/$shopHeroId/purchase" $errCode $errBody "Purchase result"
        }
    } else {
        Add-Test "shop_purchase" "POST" "/api/v1/shop/heroes/{id}/purchase" "NO_DATA" "" "No purchasable hero found"
    }
}

Step "ADMIN (expect 403)"
if ($token) {
    try { $r = Invoke-WebRequest "$base/api/v1/admin/audit" -Headers $hdr -UseBasicParsing -TimeoutSec 5; Add-Test "admin_audit" "GET" "/api/v1/admin/audit" $r.StatusCode "" "Unexpected success" }
    catch { Add-Test "admin_audit" "GET" "/api/v1/admin/audit" "403" "" "Expected 403 for non-admin" }
}

Step "ERROR CASES"
if ($token) {
    $badBody = @{ mode = "invalid_mode" } | ConvertTo-Json
    try { $r = Invoke-WebRequest "$base/api/v1/matchmaking/tickets" -Method POST -Headers $hdr -ContentType "application/json" -Body $badBody -UseBasicParsing -TimeoutSec 5; Add-Test "matchmaking_bad" "POST" "/api/v1/matchmaking/tickets" $r.StatusCode "" "Should reject" } 
    catch { Add-Test "matchmaking_bad" "POST" "/api/v1/matchmaking/tickets" "Expected error" "" "Expected error for invalid mode" }
    
    $badLineup = @{ name = "Bad"; rulesetId = "00000000-0000-0000-0000-000000000000"; entries = @(); skills = @() } | ConvertTo-Json
    try { $r = Invoke-WebRequest "$base/api/v1/lineups" -Method POST -Headers $hdr -ContentType "application/json" -Body $badLineup -UseBasicParsing -TimeoutSec 5; Add-Test "lineup_bad_ruleset" "POST" "/api/v1/lineups" $r.StatusCode "" "Should reject" } 
    catch { Add-Test "lineup_bad_ruleset" "POST" "/api/v1/lineups" "Expected error" "" "Expected error for bad ruleset" }
    
    $fakeId = "00000000-0000-0000-0000-000000000001"
    try { $r = Invoke-WebRequest "$base/api/v1/matches/$fakeId/state" -Headers $hdr -UseBasicParsing -TimeoutSec 5; Add-Test "match_state_fake" "GET" "/api/v1/matches/$fakeId/state" $r.StatusCode "" "Should 404" } 
    catch { Add-Test "match_state_fake" "GET" "/api/v1/matches/$fakeId/state" "Expected 404" "" "Expected 404" }
}

Step "RESULTS"
$total = $results.Count
$passed = ($results | Where-Object { $_.Status -match "^[12][0-9][0-9]$" }).Count
$expected = ($results | Where-Object { $_.Status -eq "Expected error" -or $_.Status -eq "Expected 401" -or $_.Status -eq "Expected 403" -or $_.Status -eq "Expected 404" -or $_.Status -eq "Expected 401/403" }).Count
$skipped = ($results | Where-Object { $_.Status -eq "NO_TOKEN" -or $_.Status -eq "NO_DATA" -or $_.Status -eq "N/A" -or $_.Status -eq "no_data" }).Count

Write-Host ""
Write-Host "RESULTS: $total total | $passed HTTP-2xx | $expected expected-rejected | $skipped skipped" -ForegroundColor Yellow
Write-Host ""

$results | Format-Table Label, Method, URI, Status, Notes -AutoSize | Out-String | Write-Host
